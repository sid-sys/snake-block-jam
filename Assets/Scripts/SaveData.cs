using System;
using System.Collections.Generic;
using UnityEngine;

namespace SnakeJam
{
    [Serializable]
    public class SaveState
    {
        public int level = 1;
        public int coins = 100;
        public int gems = 0;
        public int hints = 2;
        public int undos = 2;
        public int vacuums = 1;
        public int skin = 1;     // default look: Neon snakes on the Night board
        public int theme = 3;
        public List<int> ownedSkins = new List<int> { 1 };
        public List<int> ownedThemes = new List<int> { 3 };
        public int lastDailyDay = -1;
        public bool sfx = true;
        public bool vibration = true;
        public bool notification = true;
        public int lives = 5;
        public long nextLifeUnix = 0;
        public int giftDay = 0;
        public int lastGiftDay = -1000;
        public bool tutorialDone = false;
        public bool zoomTutorialDone = false;
        public bool seenBridge, seenPortal, seenDrill, seenFrozen, seenLight;
        public bool adminUnlimited = false;
    }

    public static class Save
    {
        const string Key = "snakejam_save_v1";
        public static SaveState S;
        public static event Action Changed;

        public static void Load()
        {
            try
            {
                string json = PlayerPrefs.GetString(Key, "");
                S = string.IsNullOrEmpty(json) ? new SaveState() : JsonUtility.FromJson<SaveState>(json);
            }
            catch { S = new SaveState(); }
            if (S == null) S = new SaveState();
        }

        public static void Commit()
        {
            try { PlayerPrefs.SetString(Key, JsonUtility.ToJson(S)); PlayerPrefs.Save(); } catch { }
            Changed?.Invoke();
        }

        public const int MaxLives = 5;
        public const int LifeSeconds = 900;
        static long Now() => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // give back lives that regenerated while away
        public static void TickLives()
        {
            if (S.lives >= MaxLives) { S.nextLifeUnix = 0; return; }
            long now = Now();
            if (S.nextLifeUnix == 0) S.nextLifeUnix = now + LifeSeconds;
            bool changed = false;
            while (S.lives < MaxLives && now >= S.nextLifeUnix) { S.lives++; S.nextLifeUnix += LifeSeconds; changed = true; }
            if (S.lives >= MaxLives) S.nextLifeUnix = 0;
            if (changed) Commit();
        }

        public static int SecondsToNextLife() => S.lives >= MaxLives || S.nextLifeUnix == 0 ? 0 : (int)Mathf.Max(0, S.nextLifeUnix - Now());

        public static void LoseLife()
        {
            if (S.lives <= 0) return;
            if (S.lives >= MaxLives) S.nextLifeUnix = Now() + LifeSeconds;
            S.lives--;
            Commit();
        }

        public static void AddLives(int n)
        {
            S.lives = Mathf.Min(MaxLives, S.lives + n);
            if (S.lives >= MaxLives) S.nextLifeUnix = 0;
            Commit();
        }

        public static void Reset()
        {
            S = new SaveState();
            Commit();
        }
    }

    public static class Palette
    {
        public static readonly string[] SkinNames = { "Candy", "Neon", "Sunset", "Ocean", "Jungle", "Red & White", "Rainbow", "Black & White", "Zebra" };
        public static readonly int[] SkinGemPrice = { 20, 0, 30, 40, 60, 50, 80, 50, 80 };
        // 0 = one colour per snake, 1 = rainbow along the body (violet head to red tail), 2 = black / white zebra stripes
        public static int SkinKind(int i) => i == 6 ? 1 : (i == 8 ? 2 : 0);

        static Color H(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        static Color[][] skins;
        public static Color[] Skin(int i)
        {
            if (skins == null)
            {
                // first five colours of every skin are the main ones: red, blue, green, yellow, violet. The last three are rare extras.
                skins = new[]
                {
                    new[] { H("#F2545B"), H("#2D9CFF"), H("#5CCB3E"), H("#FFC93C"), H("#9B5CF0"), H("#1FD1B5"), H("#FF9A2E"), H("#FF6FB1") },
                    new[] { H("#FF1744"), H("#00B0FF"), H("#00E676"), H("#FFEA00"), H("#D500F9"), H("#00F5D4"), H("#FF9100"), H("#FF2E93") },
                    new[] { H("#E53935"), H("#3F7DE0"), H("#7CB342"), H("#FFB300"), H("#8E44AD"), H("#FF7043"), H("#EC407A"), H("#FFD54F") },
                    new[] { H("#EF5350"), H("#1E88E5"), H("#2ECC71"), H("#FDD835"), H("#7E57C2"), H("#26C6DA"), H("#4DD0E1"), H("#5C6BC0") },
                    new[] { H("#D84315"), H("#1976D2"), H("#388E3C"), H("#F9A825"), H("#6A1B9A"), H("#8D6E63"), H("#C0CA33"), H("#FFA726") },
                    new[] { H("#E5383B"), H("#F8F8F8"), H("#E5383B"), H("#F8F8F8"), H("#E5383B"), H("#F8F8F8"), H("#E5383B"), H("#F8F8F8") },   // red and white only
                    new[] { H("#FF3B3B"), H("#FF9A2E"), H("#FFE23B"), H("#4CD964"), H("#2D9CFF"), H("#5856D6"), H("#B05CF0"), H("#FF3B3B") },   // rainbow (colours come from SnakeView)
                    new[] { H("#17171C"), H("#F4F4F4"), H("#17171C"), H("#F4F4F4"), H("#17171C"), H("#F4F4F4"), H("#17171C"), H("#F4F4F4") },   // black and white only
                    new[] { H("#17171C"), H("#F4F4F4"), H("#17171C"), H("#F4F4F4"), H("#17171C"), H("#F4F4F4"), H("#17171C"), H("#F4F4F4") },   // zebra stripes (pattern from SnakeView)
                };
            }
            return skins[Mathf.Clamp(i, 0, skins.Length - 1)];
        }

        public static readonly Color DotLight = new Color(0.45f, 0.55f, 0.7f, 0.55f);

        public static readonly string[] ThemeNames = { "Sky", "Mint", "Candy Land", "Night", "Paper" };
        public static readonly int[] ThemeCoinPrice = { 600, 400, 800, 0, 500 };
        // top, bottom background colours
        public static void Theme(int i, out Color top, out Color bottom, out Color tile)
        {
            switch (i)
            {
                case 1: top = H("#D9FBEA"); bottom = H("#8FE3C0"); tile = H("#FFFFFF"); break;
                case 2: top = H("#FFE0F2"); bottom = H("#FFB5DD"); tile = H("#FFFFFF"); break;
                case 3: top = H("#3A3F7A"); bottom = H("#1B1E48"); tile = H("#7C86D8"); break;
                case 4: top = H("#F6F6F8"); bottom = H("#CFCFD6"); tile = H("#FFFFFF"); break;
                default: top = H("#DDEFFF"); bottom = H("#8FC9F5"); tile = H("#FFFFFF"); break;
            }
        }
    }
}
