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

        // Persistent line ("teammate paused"). Unlike transient lines it has no expiry - it
        // represents a state, not an event - and is removed by ClearStickyNotification. A timed
        // line would leave the text on screen after resume and remove it while still paused.
        private NotificationLine stickyMessage;

        // Add a new notification to the list
        public void DisplayNotification(string notification)
        {
            ModLog.Info("Notification: " + notification);

            NotificationLine line = new NotificationLine(notification, Main.Multiplayer.config.notificationDisplaySeconds);
            currentMessages.Insert(0, line);

            // Over capacity: drop the oldest transient. The persistent line is never evicted.
            if (currentMessages.Count > MAX_LINES)
            {
                int idx = currentMessages.Count - 1;
                if (currentMessages[idx] == stickyMessage && idx > 0)
                    idx--;
                currentMessages.RemoveAt(idx);
            }
        }

        /// <summary>
        /// Shows a persistent line that does not expire until <see cref="ClearStickyNotification"/>.
        /// Idempotent on the same text: repeated calls do not add lines and do not reorder.
        /// </summary>
        public void DisplayStickyNotification(string notification)
        {
            if (string.IsNullOrEmpty(notification))
                return;

            if (stickyMessage != null && stickyMessage.Text == notification)
                return;

            ClearStickyNotification();

            ModLog.Info("Notification (persistent): " + notification);
            // PositiveInfinity and not 0: even if a future change reached the aging path, the line
            // would not expire.
            stickyMessage = new NotificationLine(notification, float.PositiveInfinity);
            currentMessages.Insert(0, stickyMessage);
        }

        /// <summary>
        /// Removes the persistent line if present. No-op when absent - the normal case (a resume
        /// without a prior pause).
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

        // Update order, text, fade and box size of every line
        public void Update()
        {
            if (messageBox == null)
                return;

            float maxWidth = 0;
            float timeBeforeFade = Main.Multiplayer.config.notificationDisplaySeconds / 2;

            for (int i = 0; i < textLines.Length; i++)
            {
                if (i >= currentMessages.Count)
                {
                    textLines[i].text = string.Empty;
                    continue;
                }

                NotificationLine currentLine = currentMessages[i];
                textLines[i].text = currentLine.Text;
                if (textLines[i].preferredWidth > maxWidth)
                    maxWidth = textLines[i].preferredWidth;

                // Persistent line: never ages and never fades - it is a state, removed by
                // ClearStickyNotification, not by the clock.
                if (currentLine == stickyMessage)
                {
                    textLines[i].color = Color.white;
                    continue;
                }

                currentLine.SubtractTime(Time.unscaledDeltaTime);
                if (currentLine.TimeLeft <= 0)
                {
                    currentMessages.RemoveAt(i);
                }
                else if (currentLine.TimeLeft <= timeBeforeFade)
                {
                    textLines[i].color = new Color(1, 1, 1, currentLine.TimeLeft / timeBeforeFade);
                }
                else
                {
                    textLines[i].color = Color.white;
                }
            }

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

            GameObject obj = new GameObject("Message box", typeof(RectTransform), typeof(Image));
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            SetOrientation(rect, new Vector2(1, 0));
            rect.sizeDelta = new Vector2(0, 0);

            Image image = obj.GetComponent<Image>();
            image.color = new Color(0.17f, 0.17f, 0.17f, 0.8f);

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