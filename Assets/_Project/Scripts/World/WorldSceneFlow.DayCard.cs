using System.Collections;
using UnityEngine;

namespace SunkCost.World
{
    // The day card (Dan, 18 September 2026: "a small screen fade and write 'day
    // x' so they know a day has passed"): when the crew ends the day at the
    // monitor every peer on the ship goes black for a moment with DAY n OF N —
    // PAYDAY after the last day — and fades back in. Presentation only, from
    // the replicated day count; the ride's own fades take precedence (the card
    // fades in only while its text is still the one on the screen).
    public sealed partial class WorldSceneFlow
    {
        private const float DayCardFadeSeconds = 0.5f;
        private Coroutine dayCard;

        // Day 1 arrives with the ship at sea (no card: the deck panel says so);
        // a fresh run puts the day back to 0 (no card).
        private void OnDayChangedForCard(int previous, int next)
        {
            if (next > previous && previous >= 1) ShowDayCard($"DAY {next} OF {Settings.DaysPerCycle}");
        }

        private void OnPaydayChangedForCard(bool payday)
        {
            if (payday) ShowDayCard("PAYDAY");
        }

        private void ShowDayCard(string text)
        {
            if (!networkManager.ClientManager.Started || ScreenFade.Instance == null || Settings.DayCardSeconds <= 0f) return;
            if (dayState != null && dayState.CabinRide.Active) return; // a ride owns the screen
            if (dayCard != null) StopCoroutine(dayCard);
            dayCard = StartCoroutine(DayCardRoutine(text));
        }

        private IEnumerator DayCardRoutine(string text)
        {
            ScreenFade fade = ScreenFade.Instance;
            fade.FadeOut(DayCardFadeSeconds, text);
            // The hold counts from the frame the screen is black (a hitch in the
            // fade must not eat the card), and only while the card is still the
            // one on the screen.
            float blackBy = Time.unscaledTime + DayCardFadeSeconds + 2f;
            while (!fade.IsBlack && fade.Text == text && Time.unscaledTime < blackBy) yield return null;
            float until = Time.unscaledTime + Settings.DayCardSeconds;
            while (Time.unscaledTime < until && fade.Text == text) yield return null;
            if (fade.Text == text) fade.FadeIn(DayCardFadeSeconds);
            dayCard = null;
        }
    }
}
