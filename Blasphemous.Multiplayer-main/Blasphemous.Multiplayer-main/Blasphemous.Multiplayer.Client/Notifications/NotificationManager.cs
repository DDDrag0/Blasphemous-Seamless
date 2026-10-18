using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.ProgressSync;
using Gameplay.UI.Others.UIGameLogic;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.Multiplayer.Client.Notifications
{
    public class NotificationManager
    {
        private const int MAX_LINES = 4;
        private const int LINE_HEIGHT = 20;

        // Created UI
        private RectTransform messageBox;
        private Text[] textLines;

        // Current message status
        private readonly List<NotificationLine> currentMessages = new ();

        // ⭐⭐ B — LA RIGA PERSISTENTE ("l'altro e' in pausa"). Le altre notifiche nascono con una
        // scadenza (`config.notificationDisplaySeconds`, 4 s) e sfumano: questa NO, perche' deve dire
        // **uno stato**, non un evento — «resta finche' l'altro e' in pausa, e sparisce quando riprende».
        // Una notifica a tempo avrebbe lasciato la frase a schermo dopo la ripresa (e l'avrebbe tolta
        // mentre l'altro era ancora in pausa): due bugie, una per lato.
        private NotificationLine stickyMessage;

        // Add a new notification to the list
        public void DisplayNotification(string notification)
        {
            ModLog.Info("Notification: " + notification);

            // Add new line to list
            NotificationLine line = new NotificationLine(notification, Main.Multiplayer.config.notificationDisplaySeconds);
            currentMessages.Insert(0, line);

            // Remove first one if overfull
            // ⚠️ La riga PERSISTENTE non si toglie mai per fare posto (vedi stickyMessage): si scarta
            // l'ultima riga a tempo, che e' quella piu' vecchia e gia' in dissolvenza.
            if (currentMessages.Count > MAX_LINES)
            {
                int idx = currentMessages.Count - 1;
                if (currentMessages[idx] == stickyMessage && idx > 0)
                    idx--;
                currentMessages.RemoveAt(idx);
            }
        }

        /// <summary>
        /// ⭐ B — Mostra una notifica che **non scade**: resta a schermo finche' non arriva
        /// <see cref="ClearStickyNotification"/>. Idempotente sullo stesso testo: chiamarla a ogni
        /// battito di conferma non crea righe in piu' e non la rianima in cima (basta che ci sia).
        /// Serve a `PauseNotice`: la frase "YOUR TEAMMATE IS PAUSED" e' uno STATO da mostrare e togliere,
        /// non un evento da far sfumare da solo.
        /// </summary>
        public void DisplayStickyNotification(string notification)
        {
            if (string.IsNullOrEmpty(notification))
                return;

            if (stickyMessage != null && stickyMessage.Text == notification)
                return;

            ClearStickyNotification();

            ModLog.Info("Notification (persistent): " + notification);
            // `float.PositiveInfinity` e non 0: se un giorno questa riga finisse nel ramo che sottrae il
            // tempo (perche' il confronto qui sotto non la riconosce piu'), non scadrebbe lo stesso.
            stickyMessage = new NotificationLine(notification, float.PositiveInfinity);
            currentMessages.Insert(0, stickyMessage);
        }

        /// <summary>
        /// ⭐ B — Toglie la riga persistente, se c'e'. Chiamarla quando non c'e' non fa niente: e' il
        /// caso normale (una ripresa ricevuta senza una pausa ricevuta).
        /// </summary>
        public void ClearStickyNotification()
        {
            if (stickyMessage == null)
                return;

            currentMessages.Remove(stickyMessage);
            stickyMessage = null;
        }

        // Add a new notification for a progress update, but calculate it first
        public void DisplayProgressNotification(string playerName, ProgressUpdate progress)
        {
            string notification = Main.Multiplayer.ProgressManager.GetProgressNotification(progress);

            if (notification != null)
                DisplayNotification($"{playerName} {notification}");
        }

        // Update the order, text, and fade of all notification lines and box size
        public void Update()
        {
            if (messageBox == null)
                return;

            // Loop over each line of text
            float maxWidth = 0;
            float timeBeforeFade = Main.Multiplayer.config.notificationDisplaySeconds / 2;

            for (int i = 0; i < textLines.Length; i++)
            {
                // There aren't this many notifications
                if (i >= currentMessages.Count)
                {
                    textLines[i].text = string.Empty;
                    continue;
                }

                // Update text
                NotificationLine currentLine = currentMessages[i];
                textLines[i].text = currentLine.Text;
                if (textLines[i].preferredWidth > maxWidth)
                    maxWidth = textLines[i].preferredWidth;

                // ⭐ B — la riga PERSISTENTE non sottrae tempo e non sfuma: e' uno stato, e lo toglie
                // `ClearStickyNotification` (da `PauseNotice`), non l'orologio.
                if (currentLine == stickyMessage)
                {
                    textLines[i].color = Color.white;
                    continue;
                }

                // Decrease the amount of time left on this notification line
                currentLine.SubtractTime(Time.unscaledDeltaTime);
                if (currentLine.TimeLeft <= 0)
                {
                    // Time is over, remove this message
                    currentMessages.RemoveAt(i);
                }
                else if (currentLine.TimeLeft <= timeBeforeFade)
                {
                    // Enough time has passed, fade this text away
                    textLines[i].color = new Color(1, 1, 1, currentLine.TimeLeft / timeBeforeFade);
                }
                else
                {
                    // This text line is pretty new, keep at full opacity
                    textLines[i].color = Color.white;
                }
            }

            // Set size of message box based on notifications
            if (maxWidth > 0)
                maxWidth += 10;
            messageBox.sizeDelta = new Vector2(maxWidth, currentMessages.Count * LINE_HEIGHT);
        }

        public void LevelLoaded()
        {
            if (messageBox == null)
                CreateUI();
        }

        // Create text background & lines
        private void CreateUI()
        {
            ModLog.Info("Creating new message box!");

            // Find canvas parent
            Transform parent = null;
            foreach (Canvas c in Object.FindObjectsOfType<Canvas>())
            {
                if (c.name == "Game UI")
                {
                    parent = c.transform;
                    break;
                }
            }
            if (parent == null)
                return;

            // Find text object
            GameObject textObject = null;
            foreach (PlayerPurgePoints purge in Object.FindObjectsOfType<PlayerPurgePoints>())
            {
                if (purge.name == "PurgePoints")
                {
                    textObject = purge.transform.GetChild(1).gameObject;
                    break;
                }
            }
            if (textObject == null)
                return;

            // Create message background
            GameObject obj = new GameObject("Message box", typeof(RectTransform), typeof(Image));
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            SetOrientation(rect, new Vector2(1, 0));
            rect.sizeDelta = new Vector2(0, 0);

            // Set image color
            Image image = obj.GetComponent<Image>();
            image.color = new Color(0.17f, 0.17f, 0.17f, 0.8f);

            // Create text lines
            textLines = new Text[MAX_LINES];
            for (int i = 0; i < MAX_LINES; i++)
            {
                Text line = Object.Instantiate(textObject, rect).GetComponent<Text>();
                SetOrientation(line.rectTransform, Vector2.zero);
                line.rectTransform.sizeDelta = new Vector2(100, 20);
                line.rectTransform.anchoredPosition = new Vector2(5, 2.5f + i * 20);
                line.raycastTarget = false;
                line.text = string.Empty;
                line.horizontalOverflow = HorizontalWrapMode.Overflow;
                line.alignment = TextAnchor.LowerLeft;
                line.color = Color.white;
                textLines[i] = line;
            }

            messageBox = rect;

            // Set a recttransform to a corner
            void SetOrientation(RectTransform rect, Vector2 orient)
            {
                rect.anchorMin = orient;
                rect.anchorMax = orient;
                rect.pivot = orient;
                rect.anchoredPosition = Vector2.zero;
            }
        }
    }
}
