using System;
using System.Collections.Generic;
using DG.Tweening;
using MoreMountains.Feedbacks;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace SnakeJam
{
    public class Game : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<Game>() == null) new GameObject("Game").AddComponent<Game>();
        }

        enum Screen { Splash, Map, Play }

        Camera cam;
        Board board;
        Canvas cv;
        SpriteRenderer bg;
        RectTransform splashRoot, mapRoot, hudRoot, nodesRoot;
        Screen screen = Screen.Splash;

        // shared chips
        readonly List<TMP_Text> coinTexts = new List<TMP_Text>();
        RectTransform hudCoinChip, mapCoinChip;
        TMP_Text hudLivesTxt, livesTxt, timerTxt, movesTxt, levelLblTxt, banner, loadingTxt;
        Image movesBox, loadFill, hand, edgeFlash;
        RectTransform topBar, bottomBar, zoomBtns;
        CanvasGroup mapGroup, hudGroup;
        TMP_Text levelBannerTxt;
        Tween movesColorTween;
        Button hintB, undoB, vacB;
        UIKit.PopupHandle shop, settings, win, modal, lab;
        string curSpecial; int curSpecialSeed;
        RectTransform previewPanel;
        SoundLab soundLab;
        bool previewShop, previewFromMap; int origTheme, origSkin;
        float rowStep = 178f, rowH = 160f;
        int shopTab;
        bool adMovesUsed;       // the free "+5 moves" ad was already used in this level attempt
        float tickT;

        // ======================================================= setup
        void Awake()
        {
            DOTween.Init(true, true, LogBehaviour.ErrorsOnly);
            DOTween.SetTweensCapacity(1500, 200);
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            Save.Load();
            Save.TickLives();

            cam = Camera.main;
            if (cam == null) { var cg = new GameObject("Main Camera"); cg.tag = "MainCamera"; cam = cg.AddComponent<Camera>(); cg.AddComponent<AudioListener>(); }
            cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor;
            // camera rig: the rig is moved to fit the board, the Feel shaker moves the camera inside it
            var rig = new GameObject("CameraRig").transform;
            cam.transform.SetParent(rig, false);
            cam.transform.localPosition = new Vector3(0, 0, -10f);
            cam.transform.localRotation = Quaternion.identity;
            if (cam.GetComponent<MMCameraShaker>() == null) cam.gameObject.AddComponent<MMCameraShaker>();

            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(cam.transform, false);
            bgGo.transform.localPosition = new Vector3(0, 0, 50);
            bg = bgGo.AddComponent<SpriteRenderer>();
            bg.sortingOrder = -100;

            var bgo = new GameObject("Board");
            board = bgo.AddComponent<Board>();
            board.Init(cam);
            board.OnWin += OnBoardWin;
            board.OnChanged += RefreshHud;
            board.OnOutOfMoves += ShowOutOfMoves;
            board.OnPraise += ShowPraise;
            board.OnHit += OnBoardHit;
            board.OnFrozenTap += left => Toast("Frozen! Let " + left + " more snake" + (left == 1 ? "" : "s") + " escape first");
            board.OnFirstEscape += OnFirstEscape;
            board.OnVacuumMode += on => { if (banner != null) banner.gameObject.SetActive(on); };

            Sfx.Create(transform);
            FeelFx.Create(transform, cam);
            cv = UIKit.MakeCanvas("UI", 10);
            soundLab = SoundLab.Create(transform, cv);
            BuildMap();
            BuildHud();
            BuildSplash();
            Save.Changed += RefreshHud;
            ApplyTheme();
            board.SetVisible(false);
            ShowSplash();
        }

        void OnDestroy() { Save.Changed -= RefreshHud; }

        void Update()
        {
            tickT += Time.unscaledDeltaTime;
            if (tickT < 0.5f) return;
            tickT = 0;
            Save.TickLives();
            RefreshLives();
        }

        void LateUpdate() { if (bg != null && bg.sprite != null) FitBackground(); }

        // ======================================================= theme
        void ApplyTheme()
        {
            Palette.Theme(Save.S.theme, out var top, out var bottom, out _);
            cam.backgroundColor = bottom;
            bg.sprite = GradientSprite(top, bottom);
            FitBackground();
        }

        static Sprite GradientSprite(Color top, Color bottom)
        {
            var tex = new Texture2D(2, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 2; x++) tex.SetPixel(x, y, Color.Lerp(bottom, top, y / 63f));
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 2, 64), new Vector2(0.5f, 0.5f), 64f);
        }

        void FitBackground()
        {
            float h = cam.orthographicSize * 2f + 1f;
            float w = h * cam.aspect + 1f;
            bg.transform.localScale = new Vector3(w / (2f / 64f), h / 1f, 1f);
        }

        float SafeTop()
        {
            float sf = Screen_W / 1080f;
            return Mathf.Max(0f, (Screen_H - UnityEngine.Screen.safeArea.yMax) / sf);
        }
        float SafeBottom()
        {
            float sf = Screen_W / 1080f;
            return Mathf.Max(0f, UnityEngine.Screen.safeArea.yMin / sf);
        }
        // canvas height in layout units (width is always 1080)
        float CanvasH => 1080f * Screen_H / Mathf.Max(1f, Screen_W);
        static float Screen_W => UnityEngine.Screen.width;
        static float Screen_H => UnityEngine.Screen.height;

        // ======================================================= splash
        void BuildSplash()
        {
            splashRoot = UIKit.Stretch(cv.transform, "Splash");
            var bgImg = splashRoot.gameObject.AddComponent<Image>();
            bgImg.sprite = Art.Get("splash_bg"); bgImg.preserveAspect = false; bgImg.raycastTarget = true;
            var arf = splashRoot.gameObject.AddComponent<AspectRatioFitter>();
            // fill whole screen with the picture (crop instead of stretching)
            splashRoot.anchorMin = splashRoot.anchorMax = new Vector2(0.5f, 0.5f);
            arf.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            arf.aspectRatio = 1024f / 1536f;

            var holder = UIKit.Stretch(splashRoot, "Holder");
            holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 0.5f);
            holder.sizeDelta = new Vector2(1080, CanvasH);

            var logo = UIKit.Icon(holder, "logo", new Vector2(960, 480), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -(CanvasH * 0.18f) - SafeTop()));
            logo.name = "Logo";

            var track = UIKit.Panel(holder, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 220 + SafeBottom()), new Vector2(760, 60), new Color(0.1f, 0.15f, 0.45f, 0.75f), "LoadTrack");
            var fillRt = UIKit.Anchored(track.transform, "Fill", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(0, 48));
            loadFill = fillRt.gameObject.AddComponent<Image>();
            loadFill.sprite = Art.Round; loadFill.type = Image.Type.Sliced; loadFill.color = new Color(1f, 0.72f, 0.18f);
            loadFill.pixelsPerUnitMultiplier = 0.9f;
            loadingTxt = UIKit.Label(holder, "Loading... 0%", 44, Color.white);
            loadingTxt.rectTransform.anchorMin = loadingTxt.rectTransform.anchorMax = new Vector2(0.5f, 0);
            loadingTxt.rectTransform.anchoredPosition = new Vector2(0, 300 + SafeBottom());
        }

        void ShowSplash()
        {
            screen = Screen.Splash;
            splashRoot.gameObject.SetActive(true);
            mapRoot.gameObject.SetActive(false);
            hudRoot.gameObject.SetActive(false);
            var fillRt = loadFill.rectTransform;
            var logo = splashRoot.Find("Holder/Logo");
            logo.localScale = Vector3.zero;
            logo.DOScale(1f, 0.6f).SetEase(Ease.OutBack);
            DOVirtual.Float(0f, 1f, 2.2f, v =>
            {
                fillRt.sizeDelta = new Vector2(Mathf.Max(0f, 748f * v), 48);
                loadingTxt.text = "Loading... " + Mathf.RoundToInt(v * 100f) + "%";
            }).SetEase(Ease.InOutSine).OnComplete(() =>
            {
                DOVirtual.DelayedCall(0.25f, () =>
                {
                    splashRoot.gameObject.SetActive(false);
                    if (!Save.S.tutorialDone && Save.S.level <= 1) StartLevel(1);
                    else ShowMap();
                });
            });
        }

        // ======================================================= level map (home)
        void BuildChip(Transform parent, string icon, Vector2 pos, Vector2 size, bool withPlus, List<TMP_Text> bucket, out RectTransform chipRt)
        {
            var chip = UIKit.Panel(parent, new Vector2(0, 1), new Vector2(0, 1), pos, size, UIKit.Chip, "Chip_" + icon);
            UIKit.Skin(chip, "chip", 78, size.y * 0.5f);
            chipRt = chip.rectTransform;
            UIKit.Icon(chip.transform, icon, new Vector2(size.y * 0.82f, size.y * 0.82f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0));
            var t = UIKit.Label(chip.transform, "0", 42, Color.white);
            UIKit.FitText(t, 42);
            t.rectTransform.offsetMin = new Vector2(size.y * 0.8f, 0);
            t.rectTransform.offsetMax = new Vector2(withPlus ? -(size.y * 0.95f) : -10, 0);
            bucket.Add(t);
            if (withPlus)
                UIKit.Pill(chip.transform, "+", "green", new Vector2(size.y * 0.8f, size.y * 0.66f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-12, 0), OpenFreeCoins, null, 40);
        }

        void BuildMap()
        {
            mapRoot = UIKit.Stretch(cv.transform, "Map");
            mapGroup = mapRoot.gameObject.AddComponent<CanvasGroup>();
            var bgImg = mapRoot.gameObject.AddComponent<Image>();
            bgImg.sprite = GradientSprite(new Color(0.20f, 0.30f, 0.86f), new Color(0.40f, 0.24f, 0.78f));
            bgImg.raycastTarget = true;
            var rnd = new System.Random(5);
            for (int i = 0; i < 9; i++)
            {
                var c = UIKit.Icon(mapRoot, "icon_gear", new Vector2(10, 10), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(rnd.Next(-480, 480), rnd.Next(-(int)(CanvasH * 0.42f), (int)(CanvasH * 0.42f))));
                c.sprite = Art.Circle; c.color = new Color(1, 1, 1, 0.06f);
                c.rectTransform.sizeDelta = Vector2.one * rnd.Next(120, 320);
            }
            float top = SafeTop(), bottom = SafeBottom();

            // top row: avatar | coins | lives | settings, equal gaps of about 74
            UIKit.Icon(mapRoot, "avatar", new Vector2(110, 110), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -(30 + top)));
            BuildChip(mapRoot, "icon_coin", new Vector2(213, -(40 + top)), new Vector2(330, 90), true, coinTexts, out mapCoinChip);

            var lc = UIKit.Panel(mapRoot, new Vector2(0, 1), new Vector2(0, 1), new Vector2(618, -(40 + top)), new Vector2(260, 90), UIKit.Chip, "LivesChip");
            UIKit.Skin(lc, "chip", 78, 45);
            UIKit.Icon(lc.transform, "icon_heart", new Vector2(72, 72), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0));
            livesTxt = UIKit.Label(lc.transform, "5", 44, Color.white);
            livesTxt.rectTransform.offsetMin = new Vector2(70, 0); livesTxt.rectTransform.offsetMax = new Vector2(-90, 0);
            timerTxt = UIKit.Label(lc.transform, "", 28, Color.white);
            timerTxt.rectTransform.offsetMin = new Vector2(120, 0); timerTxt.rectTransform.offsetMax = new Vector2(0, 0);
            timerTxt.alignment = TextAlignmentOptions.Center;

            UIKit.ImageButton(mapRoot, "btn_gear", 96, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-30, -(40 + top)), OpenSettings);

            // second row: GIFT on the left, SHOP on the right (pictures only, no words)
            UIKit.ImageButton(mapRoot, "icon_gift", 130, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -(170 + top)), OpenGift);
            UIKit.ImageButton(mapRoot, "icon_shop", 130, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-34, -(170 + top)), () => OpenShop(0));

            // bottom: only PLAY (the middle of the screen belongs to the level path)
            nodesRoot = UIKit.Stretch(mapRoot, "Nodes");
            float ny = 90 + bottom;
            UIKit.ImageButton(mapRoot, "btn_play", 420, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, ny), OnPlayPressed);
        }

        void ShowMap()
        {
            ClosePreview();
            screen = Screen.Map;
            splashRoot.gameObject.SetActive(false);
            hudRoot.gameObject.SetActive(false);
            mapRoot.gameObject.SetActive(true);
            mapGroup.alpha = 0f; mapGroup.DOFade(1f, 0.35f);
            board.SetVisible(false);
            board.inputLocked = true;
            HideHand();
            Save.TickLives();
            RefreshHud();
            BuildNodes();
            DOVirtual.DelayedCall(0.4f, MaybeShowGift);
        }

        void BuildNodes()
        {
            foreach (Transform t in nodesRoot) Destroy(t.gameObject);
            int cur = Save.S.level;
            float bottom = SafeBottom();
            // spread the 5 level nodes evenly between the PLAY button and the top buttons
            float firstY = 470f + bottom;                 // above the bottom row (SHOP / PLAY / GIFT)
            float lastY = CanvasH - 330f - SafeTop();     // below the top row
            float step = Mathf.Clamp((lastY - firstY) / 4f, 220f, 420f);
            float lineBottom = firstY - 130f;
            var line = UIKit.Panel(nodesRoot, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, lineBottom), new Vector2(26, (firstY + step * 4f + 130f) - lineBottom), new Color(0.07f, 0.08f, 0.3f, 0.7f), "Line");
            for (int i = 0; i < 5; i++)
            {
                int lvl = cur + i;
                bool hard = lvl % 10 == 0 && lvl >= 10;
                Color col = i == 0 ? (hard ? new Color(0.9f, 0.27f, 0.24f) : new Color(0.2f, 0.82f, 0.4f))
                                   : (hard ? new Color(0.72f, 0.25f, 0.3f) : new Color(0.55f, 0.58f, 0.68f));
                var node = UIKit.Panel(nodesRoot, new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(0, firstY + i * step), new Vector2(180, 180), col, "Node" + lvl);
                var nsp = Art.Get(hard ? "node_red" : (i == 0 ? "node_green" : "node_gray"));
                if (nsp != null) { node.sprite = nsp; node.type = Image.Type.Simple; node.preserveAspect = true; node.color = i == 0 || !hard ? Color.white : new Color(1f, 0.8f, 0.8f); }
                var num = UIKit.Label(node.transform, lvl.ToString(), 90, Color.white);
                if (hard)
                {
                    var tag = UIKit.Label(node.transform, "HARD", 28, Color.white);
                    tag.rectTransform.anchorMin = tag.rectTransform.anchorMax = new Vector2(0.5f, 0);
                    tag.rectTransform.anchoredPosition = new Vector2(0, 26);
                    num.rectTransform.offsetMin = new Vector2(0, 14);
                }
                node.transform.localScale = Vector3.zero;
                node.transform.DOScale(1f, 0.35f).SetEase(Ease.OutBack).SetDelay(0.05f * i);
            }
        }

        void OnPlayPressed()
        {
            if (Save.S.lives <= 0) { ShowNoLives(); return; }
            StartLevel(Save.S.level);
        }

        void RefreshLives()
        {
            if (livesTxt == null) return;
            livesTxt.text = Save.S.lives.ToString();
            if (hudLivesTxt != null) hudLivesTxt.text = Save.S.lives.ToString();
            int s = Save.SecondsToNextLife();
            timerTxt.text = s > 0 ? (s / 60).ToString("00") + ":" + (s % 60).ToString("00") : "FULL";
        }

        // ======================================================= HUD
        void BuildHud()
        {
            hudRoot = UIKit.Stretch(cv.transform, "Hud");
            hudGroup = hudRoot.gameObject.AddComponent<CanvasGroup>();
            topBar = UIKit.Stretch(hudRoot, "TopBar");
            bottomBar = UIKit.Stretch(hudRoot, "BottomBar");
            float top = SafeTop(), bottom = SafeBottom();

            // top row, four items with equal gaps: coins | moves + level | lives | settings
            BuildChip(topBar, "icon_coin", new Vector2(30, -(40 + top)), new Vector2(260, 90), true, coinTexts, out hudCoinChip);

            movesBox = UIKit.Panel(topBar, new Vector2(0, 1), new Vector2(0, 1), new Vector2(343, -(34 + top)), new Vector2(300, 120), Color.white, "MovesBox");
            UIKit.Skin(movesBox, "pill_green", 70, 60);
            movesTxt = UIKit.Label(movesBox.transform, "Moves: 0", 46, Color.white);
            movesTxt.rectTransform.offsetMin = new Vector2(0, 36);
            levelLblTxt = UIKit.Label(movesBox.transform, "Level 1", 26, Color.white);
            levelLblTxt.rectTransform.offsetMin = new Vector2(0, 14);
            levelLblTxt.rectTransform.offsetMax = new Vector2(0, -76);
            UIKit.PlainText(movesTxt); UIKit.PlainText(levelLblTxt);

            var hl = UIKit.Panel(topBar, new Vector2(0, 1), new Vector2(0, 1), new Vector2(696, -(40 + top)), new Vector2(190, 90), UIKit.Chip, "LivesChip");
            UIKit.Skin(hl, "chip", 78, 45);
            UIKit.Icon(hl.transform, "icon_heart", new Vector2(72, 72), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0));
            hudLivesTxt = UIKit.Label(hl.transform, Save.S.lives.ToString(), 46, Color.white);
            hudLivesTxt.rectTransform.offsetMin = new Vector2(70, 0);

            UIKit.ImageButton(topBar, "btn_gear", 96, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-31, -(40 + top)), OpenSettings);

            banner =UIKit.Label(hudRoot, "Tap a snake to vacuum it", 44, new Color(0.25f, 0.2f, 0.55f));
            banner.rectTransform.anchorMin = banner.rectTransform.anchorMax = new Vector2(0.5f, 1);
            banner.rectTransform.anchoredPosition = new Vector2(0, -(300 + top));
            banner.gameObject.SetActive(false);

            float by = 70 + bottom;
            // the words HINT / UNDO / VACUUM / SHOP are part of the button pictures; a small badge shows the count (or "Lv N" until unlocked)
            hintB = UIKit.PowerTile(bottomBar, "tile_orange", "icon_hint", 200, new Vector2(-260, by + 40), OnHint);
            undoB = UIKit.PowerTile(bottomBar, "tile_blue", "icon_undo", 200, new Vector2(0, by + 40), OnUndo);
            vacB = UIKit.PowerTile(bottomBar, "tile_green", "icon_vacuum", 200, new Vector2(260, by + 40), OnVacuum);

            // zoom buttons (shown on big boards)
            // the board gets the free rectangle between the top bar and the bottom bar (see Board.FitCamera)
            board.resTop = 190f + top;
            board.resBot = 230f + bottom;
            // zoom buttons: one row just above the bottom bar (only on giant maps, the board keeps that row free)
            zoomBtns = UIKit.Anchored(hudRoot, "ZoomBtns", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, board.resBot + 10f), new Vector2(420, 110));
            UIKit.Pill(zoomBtns, "+", "blue", new Vector2(120, 100), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-140, 0), () => board.ZoomBy(1.4f), null, 60);
            UIKit.Pill(zoomBtns, "-", "blue", new Vector2(120, 100), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 0), () => board.ZoomBy(1f / 1.4f), null, 60);
            UIKit.Pill(zoomBtns, "FIT", "purple", new Vector2(120, 100), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(140, 0), () => board.ResetZoom(), null, 36);
            zoomBtns.gameObject.SetActive(false);

            levelBannerTxt = UIKit.Label(hudRoot, "", 120, Color.white);
            levelBannerTxt.rectTransform.anchorMin = levelBannerTxt.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            levelBannerTxt.rectTransform.anchoredPosition = new Vector2(0, 260);

            // red screen-edge flash when a snake hits another snake
            var er = UIKit.Stretch(hudRoot, "EdgeFlash");
            edgeFlash = er.gameObject.AddComponent<Image>();
            edgeFlash.sprite = EdgeSprite(); edgeFlash.color = new Color(1, 1, 1, 0); edgeFlash.raycastTarget = false;

            var hg = UIKit.Icon(hudRoot, "hand", new Vector2(190, 200), new Vector2(0.5f, 0.5f), new Vector2(0.15f, 0.9f), Vector2.zero);
            hand = hg; hand.gameObject.SetActive(false);
        }

        void StartLevel(int lvl) { StartData(LevelGen.Build(lvl)); }

        void StartData(LevelData data)
        {
            if (data.level > 0 && Save.S.lives <= 0) { ShowNoLives(); return; }
            screen = Screen.Play;
            adMovesUsed = false;
            splashRoot.gameObject.SetActive(false);
            mapRoot.gameObject.SetActive(false);
            hudRoot.gameObject.SetActive(true);
            board.SetVisible(true);
            board.Load(data);
            ApplyTheme();
            UIKit.Skin(movesBox, data.hard ? "pill_red" : "pill_green", 70, 60);
            string title = data.level > 0 ? "Level " + data.level + (data.hard ? "  HARD" : "") : data.title;
            levelLblTxt.text = title;
            RefreshHud();
            HideHand();

            // panels slide in, level name is typed out (DOTween)
            hudGroup.alpha = 0f; hudGroup.DOFade(1f, 0.25f);
            topBar.DOKill(); bottomBar.DOKill();
            topBar.anchoredPosition = new Vector2(0, 260);
            topBar.DOAnchorPosY(0f, 0.5f).SetEase(Ease.OutBack);
            bottomBar.anchoredPosition = new Vector2(0, -260);
            bottomBar.DOAnchorPosY(0f, 0.5f).SetEase(Ease.OutBack).SetDelay(0.1f);
            levelBannerTxt.DOKill();
            levelBannerTxt.color = Color.white; levelBannerTxt.text = "";
            UIKit.Type(levelBannerTxt, data.level > 0 ? "LEVEL " + data.level : data.title.ToUpper(), 0.5f).SetEase(Ease.Linear);
            levelBannerTxt.DOFade(0f, 0.4f).SetDelay(1.4f);
            zoomBtns.gameObject.SetActive(board.NeedsZoom);

            if (!Save.S.tutorialDone && data.level == 1) DOVirtual.DelayedCall(1.6f, ShowHand);
            DOVirtual.DelayedCall(1.7f, () => CheckIntros(data));
        }

        // power-ups unlock one by one (Undo level 3, Hint level 6, Vacuum level 10); test levels have everything
        bool PowerOpen(int unlockLevel) => board == null || board.data == null || board.data.level == 0 || board.data.level >= unlockLevel;
        string PowerLabel(int unlockLevel, int count) => !PowerOpen(unlockLevel) ? "Lv " + unlockLevel : (count > 0 ? count.ToString() : "+");

        void RefreshHud()
        {
            if (Save.S == null || movesTxt == null) return;
            string c = UIKit.Compact(Save.S.coins);
            foreach (var t in coinTexts) if (t != null) t.text = c;
            UIKit.SetPillLabel(hintB, PowerLabel(Save.HintUnlock, Save.S.hints));
            UIKit.SetPillLabel(undoB, PowerLabel(Save.UndoUnlock, Save.S.undos));
            UIKit.SetPillLabel(vacB, PowerLabel(Save.VacuumUnlock, Save.S.vacuums));
            RefreshLives();
            if (board != null && board.data != null)
            {
                int ml = Mathf.Max(0, board.movesLeft);
                movesTxt.text = Save.S.adminUnlimited ? "Moves: INF" : "Moves: " + ml;
                var want = ml <= 3 && !Save.S.adminUnlimited ? new Color(1f, 0.9f, 0.3f) : Color.white;
                if (movesTxt.color != want) { if (movesColorTween != null) movesColorTween.Kill(); movesColorTween = movesTxt.DOColor(want, 0.25f); }
            }
        }

        // ---------- tutorial hand ----------
        void ShowHand()
        {
            if (screen != Screen.Play || Save.S.tutorialDone) return;
            var s = board.FindHintSnake();
            if (s == null) return;
            Vector2 sp = cam.WorldToScreenPoint(s.HeadPos);
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)cv.transform, sp, cam, out var lp);
            hand.gameObject.SetActive(true);
            hand.rectTransform.anchoredPosition = lp;
            hand.transform.DOKill();
            hand.transform.localScale = Vector3.one;
            var seq = DOTween.Sequence();
            for (int i = 0; i < 6; i++)
            {
                seq.Append(hand.transform.DOScale(0.82f, 0.18f));
                seq.Append(hand.transform.DOScale(1f, 0.18f));
                seq.AppendInterval(0.7f);
            }
        }

        static Sprite edgeSprite;
        static Sprite EdgeSprite()
        {
            if (edgeSprite != null) return edgeSprite;
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = Mathf.Abs((x + 0.5f) / N - 0.5f) * 2f, dy = Mathf.Abs((y + 0.5f) / N - 0.5f) * 2f;
                    float d = Mathf.Max(dx, dy);                       // 0 centre .. 1 edge
                    float a = Mathf.Clamp01((d - 0.62f) / 0.38f);
                    a = a * a;
                    t.SetPixel(x, y, new Color(0.95f, 0.12f, 0.16f, a));
                }
            t.Apply();
            return edgeSprite = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        }

        void OnBoardHit()
        {
            if (edgeFlash == null) return;
            edgeFlash.DOKill();
            edgeFlash.color = new Color(1, 1, 1, 1f);
            edgeFlash.DOFade(0f, 0.75f).SetEase(Ease.OutQuad);
        }

        void HideHand() { if (hand != null) { hand.transform.DOKill(); hand.gameObject.SetActive(false); } }

        void OnFirstEscape()
        {
            HideHand();
            if (!Save.S.tutorialDone) { Save.S.tutorialDone = true; Save.Commit(); }
        }

        // ---------- praise / all clear ----------
        void ShowPraise(string word)
        {
            Sfx.Play(word == "Great!" ? "v_great" : (word == "Amazing!" ? "v_amazing" : (word == "Awesome!" ? "v_awesome" : "v_perfect")));
            if (word != "Great!") FeelFx.Praise();   // Feel: soft glow flash for the bigger praise words
            Color col = word == "Great!" ? new Color(0.35f, 0.95f, 0.45f) : (word == "Amazing!" ? new Color(0.3f, 0.9f, 1f) : new Color(1f, 0.75f, 0.2f));
            var t = UIKit.Label(hudRoot, word, 76, col);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 1);
            t.rectTransform.anchoredPosition = new Vector2(0, -(250 + SafeTop()));
            t.transform.localScale = Vector3.zero;
            var seq = DOTween.Sequence();
            seq.Append(t.transform.DOScale(1f, 0.25f).SetEase(Ease.OutBack));
            seq.AppendInterval(0.6f);
            seq.Append(t.DOFade(0f, 0.3f));
            seq.OnComplete(() => { if (t != null) Destroy(t.gameObject); });
            Destroy(t.gameObject, 3f);
        }

        void OnBoardWin()
        {
            var holder = UIKit.Stretch(hudRoot, "AllClear");
            var a = UIKit.Label(holder, "ALL", 170, new Color(1f, 0.8f, 0.2f));
            a.rectTransform.anchorMin = a.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            a.rectTransform.anchoredPosition = new Vector2(0, 120);
            var b = UIKit.Label(holder, "CLEAR!", 180, new Color(0.3f, 0.85f, 1f));
            b.rectTransform.anchorMin = b.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            b.rectTransform.anchoredPosition = new Vector2(0, -60);
            holder.localScale = Vector3.zero;
            Sfx.Play("pop");
            Sfx.Play("v_allclear");
            holder.DOScale(1f, 0.4f).SetEase(Ease.OutBack);
            Destroy(holder.gameObject, 3f);
            DOVirtual.DelayedCall(1.3f, ShowComplete);
        }

        // ======================================================= complete
        void ShowComplete()
        {
            if (board.data.level == 0) { ShowTestComplete(); return; }
            int lvl = board.data.level;
            bool hard = board.data.hard;
            // normal win: 50 coins + 10 per unused move (at most +30). Boss win (every 10th level): 250 coins + one random power-up.
            bool boss = lvl % 10 == 0;
            int unused = Save.S.adminUnlimited ? 0 : Mathf.Max(0, board.movesLeft);
            int coins = boss ? 250 : 50 + Mathf.Min(30, unused * 10);
            int prize = boss ? UnityEngine.Random.Range(0, 3) : -1;           // 0 hint, 1 undo, 2 vacuum
            string[] prizeIcon = { "icon_hint", "icon_undo", "icon_vacuum" }, prizeName = { "Hint", "Undo", "Vacuum" };

            win = UIKit.Popup(cv, "", new Vector2(1000, 1500), false, 0.85f, true);
            Sfx.Play("complete");
            var c = win.content;
            var rib = UIKit.Anchored(c, "Ribbon", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -250), new Vector2(840, 426));
            var ri = rib.gameObject.AddComponent<Image>();
            ri.sprite = Art.Get("ui_ribbon"); ri.preserveAspect = true; ri.raycastTarget = false;
            var rt = UIKit.Label(rib, "", 84, Color.white);
            rt.rectTransform.offsetMin = new Vector2(0, 20); rt.rectTransform.offsetMax = new Vector2(0, 20);
            rib.localScale = Vector3.zero; rib.DOScale(1f, 0.4f).SetEase(Ease.OutBack);
            UIKit.Type(rt, "COMPLETE", 0.6f).SetDelay(0.3f).SetEase(Ease.Linear);     // typed out

            var glow = UIKit.Icon(c, "icon_gear", new Vector2(900, 900), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 0));
            glow.sprite = Art.Glow; glow.color = new Color(1f, 0.82f, 0.25f, 0.8f);
            var pile = UIKit.Icon(c, "coin_pile", new Vector2(470, 390), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 10));
            pile.transform.localScale = Vector3.zero; pile.transform.DOScale(1f, 0.5f).SetEase(Ease.OutBack).SetDelay(0.2f);
            var amt = UIKit.Label(c, "0", 110, Color.white);
            amt.rectTransform.anchorMin = amt.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            amt.rectTransform.anchoredPosition = new Vector2(0, prize >= 0 ? -170 : -190);
            DOVirtual.Float(0, coins, 0.7f, v => amt.text = Mathf.RoundToInt(v).ToString()).SetDelay(0.5f);
            if (prize >= 0)
            {
                var gl = UIKit.Label(c, "+1 " + prizeName[prize], 56, new Color(1f, 0.95f, 0.6f));
                gl.rectTransform.anchorMin = gl.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                gl.rectTransform.anchoredPosition = new Vector2(60, -272);
                var gi = UIKit.Icon(c, prizeIcon[prize], new Vector2(90, 90), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-130, -272));
                gi.transform.localScale = Vector3.zero; gi.transform.DOScale(1f, 0.4f).SetEase(Ease.OutBack).SetDelay(0.9f);
            }

            bool claimed = false;
            System.Action<int> finish = mult =>
            {
                claimed = true;
                Save.S.level = Mathf.Max(Save.S.level, lvl + 1);
                Save.S.coins += coins * mult;
                if (prize == 0) Save.S.hints++; else if (prize == 1) Save.S.undos++; else if (prize == 2) Save.S.vacuums++;
                Save.Commit();
                FlyCoins(win.card.position);
                win.Close();
                DOVirtual.DelayedCall(1.0f, () => StartLevel(Save.S.level));   // straight into the next level
            };
            UIKit.Pill(c, "Claim " + coins, "green", new Vector2(560, 160), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 250), () => { if (!claimed) finish(1); }, null, 70);
            UIKit.Pill(c, "Claim " + coins * 2 + "  AD", "orange", new Vector2(560, 120), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 90), () =>
            {
                if (claimed) return;
                ShowAd(() => finish(2));
            }, "icon_ad", 44);
        }

        void ShowTestComplete()
        {
            win = UIKit.Popup(cv, "TEST LEVEL CLEAR", new Vector2(860, 760), false);
            var c = win.content;
            UIKit.Icon(c, "icon_star", new Vector2(220, 220), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -230));
            UIKit.Pill(c, "PLAY AGAIN", "green", new Vector2(600, 120), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 200), () => { win.Close(); PlaySpecial(curSpecial); }, "icon_undo", 44);
            UIKit.Pill(c, "ADMIN LAB", "purple", new Vector2(600, 120), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 60), () => { win.Close(); ShowMap(); OpenLab(); }, "icon_gear", 44);
        }

        void FlyCoins(Vector3 from)
        {
            var chip = screen == Screen.Map ? mapCoinChip : (hudRoot.gameObject.activeSelf ? hudCoinChip : mapCoinChip);
            if (chip == null) return;
            var target = chip.position;
            for (int i = 0; i < 9; i++)
            {
                var go = new GameObject("FlyCoin", typeof(RectTransform));
                go.transform.SetParent(cv.transform, false);
                var im = go.AddComponent<Image>();
                im.sprite = Art.Get("icon_coin"); im.raycastTarget = false;
                ((RectTransform)go.transform).sizeDelta = new Vector2(90, 90);
                go.transform.position = from + new Vector3(UnityEngine.Random.Range(-0.4f, 0.4f), UnityEngine.Random.Range(-0.4f, 0.4f), 0);
                var tr = go.transform;
                var seq = DOTween.Sequence();
                seq.AppendInterval(i * 0.06f);
                seq.Append(tr.DOMove(target, 0.6f).SetEase(Ease.InBack));
                seq.Join(tr.DOScale(0.5f, 0.6f));
                seq.Join(tr.DOLocalRotate(new Vector3(0, 0, 360f), 0.6f, RotateMode.FastBeyond360));
                seq.AppendCallback(() => { Sfx.Play("coin", 0.6f, UnityEngine.Random.Range(0.9f, 1.3f)); chip.DOComplete(); chip.DOPunchScale(new Vector3(0.12f, 0.12f, 0f), 0.22f, 6, 0.5f); });
                seq.OnComplete(() => { if (go != null) Destroy(go); });
                Destroy(go, 3f);
            }
        }

        // ======================================================= power ups
        void OnHint()
        {
            if (screen != Screen.Play) return;
            if (!PowerOpen(Save.HintUnlock)) { Toast("Hint unlocks at level " + Save.HintUnlock); return; }
            if (Save.S.hints <= 0) { OfferPower(0); return; }
            if (board.UseHint()) { Save.S.hints--; Save.Commit(); }
            else Toast("No free snake to hint");
        }

        void OnUndo()
        {
            if (screen != Screen.Play) return;
            if (!PowerOpen(Save.UndoUnlock)) { Toast("Undo unlocks at level " + Save.UndoUnlock); return; }
            if (Save.S.undos <= 0) { OfferPower(1); return; }
            if (!board.CanUndo) { Toast("Nothing to undo"); return; }
            if (board.UseUndo()) { Save.S.undos--; Save.Commit(); }
        }

        void OnVacuum()
        {
            if (screen != Screen.Play) return;
            if (!PowerOpen(Save.VacuumUnlock)) { Toast("Vacuum unlocks at level " + Save.VacuumUnlock); return; }
            if (Save.S.vacuums <= 0) { OfferPower(2); return; }
            board.SetMode(board.mode == Board.Mode.Vacuum ? Board.Mode.Normal : Board.Mode.Vacuum);
        }

        // player has none of this power-up: buy one with coins, or watch an ad for one free use (2 ads per day for all power-ups)
        static readonly int[] PowerPrice = { 150, 100, 250 };          // hint, undo, vacuum
        static readonly string[] PowerName = { "HINT", "UNDO", "VACUUM" };
        static readonly string[] PowerIcon = { "icon_hint", "icon_undo", "icon_vacuum" };

        void GivePower(int kind, int n)
        {
            if (kind == 0) Save.S.hints += n; else if (kind == 1) Save.S.undos += n; else Save.S.vacuums += n;
            Save.Commit();
        }

        void OfferPower(int kind)
        {
            if (modal != null && modal.dim != null) return;
            Save.CheckAdDay();
            modal = UIKit.Popup(cv, "NO " + PowerName[kind], new Vector2(900, 760));
            var c = modal.content;
            UIKit.Icon(c, PowerIcon[kind], new Vector2(190, 190), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -230));
            UIKit.Pill(c, "BUY 1   " + PowerPrice[kind], "green", new Vector2(700, 130), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 215), () =>
            {
                if (!Spend(PowerPrice[kind])) return;
                GivePower(kind, 1); modal.Close();
            }, "icon_coin", 46);
            int left = Save.AdPowerPerDay - Save.S.adPower;
            var ad = UIKit.Pill(c, left > 0 ? "1 FREE USE  AD  (" + left + " left)" : "AD LIMIT TODAY", left > 0 ? "orange" : "blue", new Vector2(700, 130), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 55), () =>
            {
                Save.CheckAdDay();
                if (Save.S.adPower >= Save.AdPowerPerDay) { Toast("No more free uses today"); return; }
                ShowAd(() => { Save.S.adPower++; GivePower(kind, 1); modal.Close(); });
            }, "icon_ad", 38);
            if (left <= 0) ad.GetComponent<Image>().color = new Color(0.7f, 0.7f, 0.75f, 0.9f);
        }

        // ======================================================= lose / quit / lives
        void ShowOutOfMoves()
        {
            if (screen != Screen.Play || (modal != null && modal.dim != null)) return;
            modal = UIKit.Popup(cv, "OUT OF MOVES", new Vector2(900, 900), false);
            Sfx.Play("fail");
            var c = modal.content;
            // first time in this attempt: free with an ad. After that: 300 coins.
            if (!adMovesUsed)
            {
                UIKit.Pill(c, "+5 MOVES  AD", "orange", new Vector2(700, 130), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -260), () =>
                    ShowAd(() => { adMovesUsed = true; modal.Close(); board.AddMoves(5); }), "icon_ad", 46);
            }
            UIKit.Pill(c, "+5 MOVES  300", "green", new Vector2(700, 130), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, adMovesUsed ? -260 : -430), () =>
            {
                if (Spend(300)) { modal.Close(); board.AddMoves(5); }
            }, "icon_coin", 46);
            UIKit.Pill(c, board.data.level == 0 ? "BACK TO LAB" : "GIVE UP  -1 LIFE", "purple", new Vector2(700, 130), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, adMovesUsed ? -430 : -640), () =>
            {
                modal.Close();
                if (board.data.level == 0) { ShowMap(); OpenLab(); } else { Save.LoseLife(); ShowMap(); }
            }, "icon_heart", 44);
        }

        void ShowQuitConfirm()
        {
            var p = UIKit.Popup(cv, "QUIT LEVEL?", new Vector2(860, 720));
            UIKit.Icon(p.content, "icon_heart", new Vector2(220, 220), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -230));
            var t = UIKit.Label(p.content, board.data.level == 0 ? "Leave this test level?" : "You will lose 1 life!", 50, UIKit.Ink, TextAnchor.MiddleCenter, false);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 1);
            t.rectTransform.anchoredPosition = new Vector2(0, -470);
            UIKit.Pill(p.content, "QUIT", "orange", new Vector2(520, 120), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 50), () =>
            {
                p.Close(); if (settings != null) settings.Close();
                if (board.data.level == 0) { ShowMap(); OpenLab(); } else { Save.LoseLife(); ShowMap(); }
            }, null, 56);
        }

        void ShowNoLives()
        {
            var p = UIKit.Popup(cv, "NO LIVES", new Vector2(900, 880));
            UIKit.Icon(p.content, "icon_heart", new Vector2(200, 200), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -230));
            int s = Save.SecondsToNextLife();
            var t = UIKit.Label(p.content, "Next life in " + (s / 60).ToString("00") + ":" + (s % 60).ToString("00"), 42, UIKit.Ink, TextAnchor.MiddleCenter, false);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 1);
            t.rectTransform.anchoredPosition = new Vector2(0, -420);
            Save.CheckAdDay();
            UIKit.Pill(p.content, "REFILL  400", "green", new Vector2(640, 120), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 190), () =>
            { if (Spend(400)) { Save.AddLives(5); p.Close(); } }, "icon_coin", 44);
            int left = Save.AdLivesPerDay - Save.S.adLives;
            var ad = UIKit.Pill(p.content, left > 0 ? "+1 LIFE  AD  (" + left + " left)" : "AD LIMIT TODAY", left > 0 ? "orange" : "blue", new Vector2(640, 120), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 50), () =>
            {
                Save.CheckAdDay();
                if (Save.S.adLives >= Save.AdLivesPerDay) { Toast("No more free lives today"); return; }
                ShowAd(() => { Save.S.adLives++; Save.AddLives(1); p.Close(); });
            }, "icon_ad", 40);
            if (left <= 0) ad.GetComponent<Image>().color = new Color(0.7f, 0.7f, 0.75f, 0.9f);
        }

        // ======================================================= ads (placeholder)
        // Replace the body of ShowAd with a real rewarded ad SDK call. onReward must run only when the ad was watched.
        void ShowAd(Action onReward)
        {
            var p = UIKit.Popup(cv, "", new Vector2(760, 420), false);
            var t = UIKit.Label(p.content, "Watching ad...", 60, UIKit.Ink, TextAnchor.MiddleCenter, false);
            UIKit.Icon(p.content, "icon_ad", new Vector2(150, 150), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -90));
            t.rectTransform.offsetMin = new Vector2(0, -80);
            DOVirtual.Float(2f, 0f, 2f, v => { if (t != null) t.text = "Watching ad... " + Mathf.CeilToInt(v); }).SetEase(Ease.Linear)
                .OnComplete(() => { p.Close(); onReward?.Invoke(); });
        }

        void Toast(string s)
        {
            var p = UIKit.Panel(cv.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 330), new Vector2(760, 100), new Color(0.12f, 0.1f, 0.3f, 0.92f), "Toast");
            UIKit.Label(p.transform, s, 42, Color.white, TextAnchor.MiddleCenter, false);
            p.transform.localScale = Vector3.zero;
            var seq = DOTween.Sequence();
            seq.Append(p.transform.DOScale(1f, 0.2f).SetEase(Ease.OutBack));
            seq.AppendInterval(1.2f);
            seq.Append(p.transform.DOScale(0f, 0.15f));
            seq.OnComplete(() => { if (p != null) Destroy(p.gameObject); });
            Destroy(p.gameObject, 2.2f);
        }

        // ======================================================= settings
        void OpenSettings()
        {
            if (settings != null && settings.dim != null) return;
            settings = UIKit.Popup(cv, "SETTINGS", new Vector2(880, 1120));
            var c = settings.content;
            Button MakeToggle(string name, Func<bool> get, Action<bool> set, string icon, float y)
            {
                Button b = null;
                b = UIKit.Pill(c, name + ": " + (get() ? "ON" : "OFF"), "blue", new Vector2(640, 120), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), null, icon, 42);
                b.onClick.AddListener(() => { set(!get()); Save.Commit(); UIKit.SetPillLabel(b, name + ": " + (get() ? "ON" : "OFF")); });
                return b;
            }
            MakeToggle("SOUND", () => Save.S.sfx, v => Save.S.sfx = v, "icon_gear", -230);
            MakeToggle("VIBRATION", () => Save.S.vibration, v => Save.S.vibration = v, "icon_gear", -380);
            MakeToggle("NOTIFICATION", () => Save.S.notification, v => Save.S.notification = v, "icon_gear", -530);
            if (screen == Screen.Play)
                UIKit.Pill(c, "QUIT LEVEL", "orange", new Vector2(640, 120), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -700), ShowQuitConfirm, "icon_heart", 42);
            else
                UIKit.Pill(c, "PLAY LEVEL " + Save.S.level, "green", new Vector2(640, 120), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -700), () => { settings.Close(); StartLevel(Save.S.level); }, "icon_play", 40);
            // hidden admin door: tap the SETTINGS title 7 times (no sound, no shake, nothing visible), then enter the secret year
            int titleTaps = 0;
            var door = UIKit.Anchored(c, "TitleDoor", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, 0), new Vector2(560, 150));
            var doorImg = door.gameObject.AddComponent<Image>(); doorImg.color = new Color(0, 0, 0, 0);
            var doorBtn = door.gameObject.AddComponent<Button>(); doorBtn.transition = Selectable.Transition.None; doorBtn.targetGraphic = doorImg;
            doorBtn.onClick.AddListener(() => { if (++titleTaps >= 7) { titleTaps = 0; OpenAdminPin(); } });
            UIKit.Pill(c, "RESET TUTORIALS", "blue", new Vector2(640, 110), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -870), () =>
            {
                Save.S.tutorialDone = false; Save.S.zoomTutorialDone = false;
                Save.S.seenBridge = Save.S.seenPortal = Save.S.seenDrill = Save.S.seenFrozen = Save.S.seenLight = false; Save.Commit(); Toast("Tutorials reset");
            }, "icon_undo", 38);
        }

        const string AdminYear = "1996";
        void OpenAdminPin()
        {
            var pin = UIKit.Popup(cv, "ENTER YEAR", new Vector2(780, 1180), true, 0.75f);
            var c = pin.content;
            string typed = "";
            var shown = UIKit.Label(c, "- - - -", 90, Color.white, TextAnchor.UpperCenter);
            shown.rectTransform.offsetMin = Vector2.zero; shown.rectTransform.offsetMax = new Vector2(0, -230);
            void Refresh() { var s = ""; for (int i = 0; i < 4; i++) s += (i < typed.Length ? typed[i].ToString() : "-") + (i < 3 ? " " : ""); shown.text = s; }
            void Press(string d)
            {
                if (d == "C") typed = "";
                else if (typed.Length < 4) typed += d;
                Refresh();
                if (typed.Length == 4)
                {
                    if (typed == AdminYear) { pin.Close(); if (settings != null) settings.Close(); OpenLab(); }
                    else { typed = ""; Refresh(); }
                }
            }
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "C", "0", "" };
            for (int i = 0; i < 12; i++)
            {
                if (keys[i] == "") continue;
                string k = keys[i];
                float x = (i % 3 - 1) * 220f, y = -440f - (i / 3) * 150f;
                UIKit.Pill(c, k, k == "C" ? "orange" : "blue", new Vector2(190, 120), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(x, y), () => Press(k), null, 56);
            }
        }

        // ======================================================= admin lab (test everything without playing the levels)
        void PlaySpecial(string key)
        {
            curSpecial = key; curSpecialSeed = UnityEngine.Random.Range(0, 900);
            StartData(LevelGen.BuildSpecial(key, curSpecialSeed));
        }

        void OpenLab()
        {
            if (lab != null && lab.dim != null) return;
            lab = UIKit.Popup(cv, "ADMIN LAB", new Vector2(1000, 2150));
            var c = lab.content;
            var tt = UIKit.Label(c, "Test every mechanic without playing the levels", 30, UIKit.Ink, TextAnchor.UpperCenter, false);
            tt.rectTransform.offsetMin = Vector2.zero; tt.rectTransform.offsetMax = new Vector2(0, -130);
            string[] cols = { "blue", "green", "purple", "orange" };
            for (int i = 0; i < LevelGen.SpecialKeys.Length; i++)
            {
                int idx = i;
                float x = (i % 3 - 1) * 330; float y = -200 - (i / 3) * 125;
                UIKit.Pill(c, LevelGen.SpecialTitles[i], cols[(i / 3) % 4], new Vector2(310, 105), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(x, y), () => { lab.Close(); PlaySpecial(LevelGen.SpecialKeys[idx]); }, null, 30);
            }
            float y0 = -200 - ((LevelGen.SpecialKeys.Length + 2) / 3) * 125 - 20;
            int jump = Save.S.level;
            var jl = UIKit.Label(c, "Level " + jump, 52, UIKit.Ink, TextAnchor.MiddleCenter, false);
            jl.rectTransform.anchorMin = jl.rectTransform.anchorMax = new Vector2(0.5f, 1);
            jl.rectTransform.anchoredPosition = new Vector2(0, y0 - 60);
            Action<int> setJump = d => { jump = Mathf.Max(1, jump + d); jl.text = "Level " + jump; };
            UIKit.Pill(c, "-10", "blue", new Vector2(130, 90), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-380, y0 - 60), () => setJump(-10), null, 36);
            UIKit.Pill(c, "-1", "blue", new Vector2(110, 90), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-250, y0 - 60), () => setJump(-1), null, 36);
            UIKit.Pill(c, "+1", "blue", new Vector2(110, 90), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(250, y0 - 60), () => setJump(1), null, 36);
            UIKit.Pill(c, "+10", "blue", new Vector2(130, 90), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(380, y0 - 60), () => setJump(10), null, 36);
            UIKit.Pill(c, "PLAY THIS LEVEL", "green", new Vector2(700, 105), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y0 - 170), () =>
            {
                lab.Close(); Save.S.level = jump; Save.Commit(); StartLevel(jump);
            }, "icon_play", 40);
            float y1 = y0 - 300;
            UIKit.Pill(c, "+9 POWER-UPS", "orange", new Vector2(450, 100), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-240, y1), () => { Save.S.hints += 9; Save.S.undos += 9; Save.S.vacuums += 9; Save.Commit(); Toast("+9 hint, undo, vacuum"); }, null, 34);
            UIKit.Pill(c, "+9999 COINS", "orange", new Vector2(450, 100), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(240, y1), () => { Save.S.coins += 9999; Save.Commit(); Toast("+9999 coins"); }, null, 34);
            UIKit.Pill(c, "COINS = 0", "purple", new Vector2(450, 100), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-240, y1 - 118), () => { Save.S.coins = 0; Save.Commit(); Toast("Coins set to 0"); }, null, 34);
            UIKit.Pill(c, "FULL LIVES", "purple", new Vector2(450, 100), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(240, y1 - 118), () => { Save.AddLives(5); Toast("Lives full"); }, null, 34);
            Button unl = null;
            unl = UIKit.Pill(c, Save.S.adminUnlimited ? "UNLIMITED MOVES: ON" : "UNLIMITED MOVES: OFF", "green", new Vector2(450, 100), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-240, y1 - 236), null, null, 28);
            unl.onClick.AddListener(() => { Save.S.adminUnlimited = !Save.S.adminUnlimited; Save.Commit(); UIKit.SetPillLabel(unl, Save.S.adminUnlimited ? "UNLIMITED MOVES: ON" : "UNLIMITED MOVES: OFF"); });
            UIKit.Pill(c, "RESET ALL SAVE", "orange", new Vector2(450, 100), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(240, y1 - 236), () => { Save.Reset(); Toast("Save reset"); lab.Close(); ShowMap(); }, null, 30);
            UIKit.Pill(c, "LEVEL MAP", "blue", new Vector2(450, 100), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-240, y1 - 354), () => { lab.Close(); ShowMap(); }, null, 34);
            UIKit.Pill(c, "PREVIEW ALL THEMES + SKINS", "purple", new Vector2(940, 100), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y1 - 472), () => { lab.Close(); OpenPreview(); }, null, 36);
            UIKit.Pill(c, "SOUND TEST", "green", new Vector2(940, 100), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y1 - 590), () => { lab.Close(); soundLab.Open(OpenLab); }, null, 40);
            UIKit.Pill(c, "RESET TUTORIALS", "blue", new Vector2(450, 100), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(240, y1 - 354), () =>
            {
                Save.S.tutorialDone = false; Save.S.zoomTutorialDone = false;
                Save.S.seenBridge = Save.S.seenPortal = Save.S.seenDrill = Save.S.seenFrozen = Save.S.seenLight = false; Save.Commit(); Toast("Tutorials reset");
            }, null, 30);
            // Feel package effects: switch them off / on, and try each one without playing a level
            Button feel = null;
            feel = UIKit.Pill(c, FeelFx.Enabled ? "FEEL EFFECTS: ON" : "FEEL EFFECTS: OFF", "green", new Vector2(940, 100), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y1 - 708), null, null, 38);
            feel.onClick.AddListener(() => { FeelFx.Enabled = !FeelFx.Enabled; UIKit.SetPillLabel(feel, FeelFx.Enabled ? "FEEL EFFECTS: ON" : "FEEL EFFECTS: OFF"); });
            UIKit.Pill(c, "TEST HIT", "orange", new Vector2(300, 100), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-320, y1 - 826), () => { FeelFx.Hit(); Fx.Shake(0.25f, 0.35f); Sfx.Play("bump"); }, null, 32);
            UIKit.Pill(c, "TEST WIN", "purple", new Vector2(300, 100), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y1 - 826), () => { FeelFx.Win(); Sfx.Play("win"); }, null, 32);
            UIKit.Pill(c, "TEST BUZZ", "blue", new Vector2(300, 100), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(320, y1 - 826), () => { FeelFx.Haptic("bump"); Toast(Save.S.vibration ? "Buzz sent (phone only)" : "Vibration is OFF in Settings"); }, null, 32);
        }

        // ======================================================= instant theme + skin preview (docked panel, board stays visible)
        // shopMode = a real player trying items before buying: previews are temporary, unowned ones are reverted on DONE
        void OpenPreview(bool shopMode = false)
        {
            if (previewPanel != null) return;
            previewShop = shopMode; previewFromMap = screen != Screen.Play;
            origTheme = Save.S.theme; origSkin = Save.S.skin;
            if (screen != Screen.Play) PlaySpecial("flower");
            DOVirtual.DelayedCall(previewFromMap ? 0.5f : 0.05f, BuildPreview);
        }

        void ClosePreview()
        {
            if (previewPanel == null) return;
            Destroy(previewPanel.gameObject);
            previewPanel = null;
            if (previewShop)
            {
                previewShop = false;
                if (!Save.S.ownedThemes.Contains(Save.S.theme)) Save.S.theme = origTheme;
                if (!Save.S.ownedSkins.Contains(Save.S.skin)) Save.S.skin = origSkin;
                Save.Commit(); ApplyTheme(); board.ApplyLook();
            }
        }

        void BuildPreview()
        {
            if (previewPanel != null) Destroy(previewPanel.gameObject);
            var p = UIKit.Panel(cv.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 230), new Vector2(1040, 590), new Color(1f, 0.98f, 0.94f, 0.97f), "PreviewPanel");
            p.raycastTarget = true;
            previewPanel = p.rectTransform;
            var t1 = UIKit.Label(p.transform, previewShop ? "TRY BEFORE YOU BUY" : "BOARD THEMES", 34, UIKit.Ink, TextAnchor.UpperCenter, false);
            t1.rectTransform.offsetMax = new Vector2(0, -16);
            string[] tc = { "blue", "green", "purple", "orange", "blue" };
            for (int i = 0; i < Palette.ThemeNames.Length; i++)
            {
                int idx = i;
                bool own = !previewShop || Save.S.ownedThemes.Contains(i);
                string lbl = own ? Palette.ThemeNames[i] : Palette.ThemeNames[i] + "\n" + Palette.ThemeCoinPrice[i] + " coins";
                var b = UIKit.Pill(p.transform, lbl, tc[i], new Vector2(190, 95), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2((i - 2) * 200f, -70), () =>
                {
                    Save.S.theme = idx; if (!previewShop) Save.Commit();
                    ApplyTheme(); board.ApplyLook(); BuildPreview();
                }, null, 26);
                if (Save.S.theme != i) b.GetComponent<Image>().color = new Color(1, 1, 1, 0.5f);
            }
            var t2 = UIKit.Label(p.transform, "SNAKE SKINS", 34, UIKit.Ink, TextAnchor.UpperCenter, false);
            t2.rectTransform.offsetMax = new Vector2(0, -190);
            string[] sc = { "orange", "blue", "purple", "green" };
            for (int i = 0; i < Palette.SkinNames.Length; i++)
            {
                int idx = i;
                bool own = !previewShop || Save.S.ownedSkins.Contains(i);
                string lbl = own ? Palette.SkinNames[i] : Palette.SkinNames[i] + "\n" + Palette.SkinCoinPrice[i] + " coins";
                float x = i < 5 ? (i - 2) * 200f : (i - 5 - 1.5f) * 200f;
                float y = i < 5 ? -245f : -345f;
                var b = UIKit.Pill(p.transform, lbl, sc[i % 4], new Vector2(190, 95), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(x, y), () =>
                {
                    Save.S.skin = idx; if (!previewShop) Save.Commit();
                    board.ApplyLook(); BuildPreview();
                }, null, 24);
                if (Save.S.skin != i) b.GetComponent<Image>().color = new Color(1, 1, 1, 0.5f);
            }
            bool buyT = previewShop && !Save.S.ownedThemes.Contains(Save.S.theme);
            bool buyS = previewShop && !Save.S.ownedSkins.Contains(Save.S.skin);
            if (buyT)
                UIKit.Pill(p.transform, "BUY THEME " + Palette.ThemeCoinPrice[Save.S.theme], "orange", new Vector2(330, 95), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-360, 22), () =>
                {
                    int t = Save.S.theme;
                    if (Spend(Palette.ThemeCoinPrice[t])) { Save.S.ownedThemes.Add(t); Save.S.theme = t; Save.Commit(); Toast("Theme unlocked!"); BuildPreview(); }
                }, "icon_coin", 28);
            if (buyS)
                UIKit.Pill(p.transform, "BUY SKIN " + Palette.SkinCoinPrice[Save.S.skin], "purple", new Vector2(330, 95), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(360, 22), () =>
                {
                    int s = Save.S.skin;
                    if (Spend(Palette.SkinCoinPrice[s])) { Save.S.ownedSkins.Add(s); Save.S.skin = s; Save.Commit(); Toast("Skin unlocked!"); BuildPreview(); }
                }, "icon_coin", 28);
            UIKit.Pill(p.transform, "DONE", "green", new Vector2(300, 95), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 22), () =>
            {
                bool shopM = previewShop, fromMap = previewFromMap;
                ClosePreview();
                if (shopM) { if (fromMap) ShowMap(); else OpenShop(shopTab); }
                else OpenLab();
            }, null, 42);
        }

        // ======================================================= mechanic + zoom tutorials
        void CheckIntros(LevelData data)
        {
            if (screen != Screen.Play || (modal != null && modal.dim != null)) return;
            if (data.HasBridge && !Save.S.seenBridge) { Save.S.seenBridge = true; Save.Commit(); ShowMechanicIntro("UNDERPASS", "A snake can drive through the tunnel under a road. Other snakes can ride along the road.", "rp_h1"); return; }
            if (data.HasPortal && !Save.S.seenPortal) { Save.S.seenPortal = true; Save.Commit(); ShowMechanicIntro("PORTAL", "A snake that goes into one portal comes out of the other portal.", "portal"); return; }
            if (data.HasDrill && !Save.S.seenDrill) { Save.S.seenDrill = true; Save.Commit(); ShowMechanicIntro("DRILL SNAKE", "A snake with a drill breaks one rock in front of it, then keeps going.", "icon_drill"); return; }
            if (data.HasFrozen && !Save.S.seenFrozen) { Save.S.seenFrozen = true; Save.Commit(); ShowMechanicIntro("FROZEN SNAKE", "A frozen snake wakes up after some other snakes have escaped.", "ice_head"); return; }
            if (data.HasLight && !Save.S.seenLight) { Save.S.seenLight = true; Save.Commit(); ShowMechanicIntro("TRAFFIC LIGHT", "A red light blocks snakes. Every tap flips all lights: red to green, green to red.", "light_red"); return; }
            if (board.NeedsZoom && !Save.S.zoomTutorialDone) ShowZoomTutorial();
        }

        void ShowMechanicIntro(string title, string text, string icon)
        {
            modal = UIKit.Popup(cv, "NEW: " + title, new Vector2(900, 980), false);
            var c = modal.content;
            var ic = UIKit.Icon(c, icon, new Vector2(300, 300), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -250));
            ic.transform.localScale = Vector3.zero; ic.transform.DOScale(1f, 0.5f).SetEase(Ease.OutBack).SetDelay(0.2f);
            var t = UIKit.Label(c, "", 44, UIKit.Ink, TextAnchor.UpperCenter, false);
            t.textWrappingMode = TextWrappingModes.Normal;
            t.rectTransform.offsetMin = new Vector2(60, 170); t.rectTransform.offsetMax = new Vector2(-60, -600);
            UIKit.Type(t, text, 1.6f).SetEase(Ease.Linear).SetDelay(0.3f);          // typewriter
            UIKit.Pill(c, "OK", "green", new Vector2(420, 120), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 40), () => { modal.Close(); DOVirtual.DelayedCall(0.4f, () => CheckIntros(board.data)); }, null, 56);
        }

        void ShowZoomTutorial()
        {
            Save.S.zoomTutorialDone = true; Save.Commit();
            modal = UIKit.Popup(cv, "ZOOM IN AND OUT", new Vector2(900, 1100), false);
            var c = modal.content;
            var h1 = UIKit.Icon(c, "hand", new Vector2(190, 200), new Vector2(0.5f, 1), new Vector2(0.15f, 0.9f), new Vector2(-40, -480));
            var h2 = UIKit.Icon(c, "hand", new Vector2(190, 200), new Vector2(0.5f, 1), new Vector2(0.15f, 0.9f), new Vector2(40, -560));
            h2.transform.localRotation = Quaternion.Euler(0, 0, 180f);
            var a1 = h1.rectTransform; var a2 = h2.rectTransform;
            var seq = DOTween.Sequence();
            for (int i = 0; i < 4; i++)
            {
                seq.Append(DOTween.To(() => 0f, v => { a1.anchoredPosition = new Vector2(-40 - v * 150, -480 + v * 110); a2.anchoredPosition = new Vector2(40 + v * 150, -560 - v * 110); }, 1f, 0.8f).SetEase(Ease.InOutSine));
                seq.AppendInterval(0.3f);
            }
            var t = UIKit.Label(c, "", 42, UIKit.Ink, TextAnchor.UpperCenter, false);
            t.textWrappingMode = TextWrappingModes.Normal;
            t.rectTransform.offsetMin = new Vector2(60, 190); t.rectTransform.offsetMax = new Vector2(-60, -760);
            UIKit.Type(t, "Big board! Pinch with two fingers to zoom. Drag to move around. You can also use the + - FIT buttons or the mouse wheel.", 3f).SetEase(Ease.Linear).SetDelay(0.3f);
            UIKit.Pill(c, "GOT IT", "green", new Vector2(420, 120), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 40), () => modal.Close(), null, 52);
        }

        // ======================================================= daily gift
        static int Today() => (int)(DateTime.UtcNow - new DateTime(2024, 1, 1)).TotalDays;
        // 7 day streak: days 1, 2, 4, 5, 6 are just a stamp. Day 3 and day 7 are the big chests.
        static readonly string[] GiftLabels = { "STAMP", "STAMP", "300 + Undo", "STAMP", "STAMP", "STAMP", "1000 + Vacuum + Hint" };
        static readonly string[] GiftIcons = { "icon_star", "icon_star", "icon_coin", "icon_star", "icon_star", "icon_star", "icon_gift" };

        void MaybeShowGift()
        {
            if (screen == Screen.Map && Save.S.lastGiftDay != Today() && (modal == null || modal.dim == null)) OpenGift();
        }

        void OpenGift()
        {
            if (modal != null && modal.dim != null) return;
            bool can = Save.S.lastGiftDay != Today();
            if (can && Save.S.lastGiftDay > -1000 && Save.S.lastGiftDay < Today() - 1) Save.S.giftDay = 0;   // missed a day: the streak starts again
            modal = UIKit.Popup(cv, "DAILY GIFT", new Vector2(940, 1300));
            var c = modal.content;
            for (int i = 0; i < 7; i++)
            {
                int row = i < 6 ? i / 3 : 2;
                int col = i < 6 ? i % 3 : 0;
                bool big = i == 6;
                var size = big ? new Vector2(820, 190) : new Vector2(250, 250);
                var pos = big ? new Vector2(0, -250 - 2 * 280 - 80) : new Vector2((col - 1) * 275, -250 - row * 280);
                bool done = i < Save.S.giftDay || (i == Save.S.giftDay && !can);
                bool today = i == Save.S.giftDay && can;
                var tile = UIKit.Panel(c, new Vector2(0.5f, 1), new Vector2(0.5f, 1), pos + new Vector2(0, 0), size,
                    today ? new Color(0.6f, 0.9f, 0.55f) : (done ? new Color(0.78f, 0.78f, 0.82f) : new Color(0.95f, 0.91f, 0.82f)), "Gift" + i);
                var d = UIKit.Label(tile.transform, "DAY " + (i + 1), 26, UIKit.Ink, TextAnchor.UpperCenter, false);
                d.rectTransform.offsetMax = new Vector2(0, -12);
                var ic = UIKit.Icon(tile.transform, GiftIcons[i], new Vector2(110, 110), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(big ? -310 : 0, big ? -8 : 6));
                var lb = UIKit.Label(tile.transform, done ? "DONE" : GiftLabels[i], i == 2 || i == 6 ? (big ? 36 : 28) : 34, UIKit.Ink, big ? TextAnchor.MiddleCenter : TextAnchor.LowerCenter, false);
                if (big) { lb.rectTransform.offsetMin = new Vector2(210, 0); lb.rectTransform.offsetMax = new Vector2(-20, -20); } else lb.rectTransform.offsetMax = new Vector2(0, 0);
                if (done) ic.color = new Color(1, 1, 1, 0.45f);
            }
            var claim = UIKit.Pill(c, can ? (Save.S.giftDay == 2 || Save.S.giftDay == 6 ? "CLAIM" : "CHECK IN") : "COME BACK TOMORROW", can ? "green" : "blue", new Vector2(640, 130), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 40), () =>
            {
                if (Save.S.lastGiftDay == Today()) return;
                int day = Save.S.giftDay;
                switch (day)
                {
                    case 2: Save.S.coins += 300; Save.S.undos += 1; break;
                    case 6: Save.S.coins += 1000; Save.S.vacuums += 1; Save.S.hints += 1; break;
                    default: break;       // stamp only
                }
                Save.S.lastGiftDay = Today(); Save.S.giftDay = (day + 1) % 7;
                Sfx.Play("buy"); Save.Commit(); FlyCoins(modal.card.position); modal.Close();
            }, null, 46);
        }

        // ======================================================= shop
        void OpenShop(int tab)
        {
            if (shop != null && shop.dim != null) return;
            shopTab = tab;
            shop = UIKit.Popup(cv, "SHOP", new Vector2(980, 1640));
            BuildShopTabs();
        }

        RectTransform shopBody;
        void BuildShopTabs()
        {
            var c = shop.content;
            foreach (Transform t in c) if (t.name.StartsWith("Pill_") || t.name == "ShopBody") Destroy(t.gameObject);
            string[] tabs = { "POWER", "SKINS", "THEMES" };
            string[] cols = { "blue", "purple", "orange" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var tb = UIKit.Pill(c, tabs[i], cols[i], new Vector2(290, 92), new Vector2(0, 1), new Vector2(0, 1), new Vector2(34 + i * 305, -150), () => { shopTab = idx; BuildShopTabs(); }, null, 38);
                if (i != shopTab) tb.GetComponent<Image>().color = new Color(1, 1, 1, 0.45f);
            }
            // rows share the free height evenly (no empty block at the bottom)
            int rowsInTab = shopTab == 0 ? 7 : (shopTab == 1 ? Palette.SkinNames.Length : Palette.ThemeNames.Length);
            float bodyH = 1640f - 270f - 50f;
            rowStep = Mathf.Min(bodyH / rowsInTab, 230f); rowH = rowStep - 14f;
            shopBody = UIKit.Anchored(c, "ShopBody", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -270), new Vector2(920, bodyH));
            switch (shopTab)
            {
                case 0: TabPower(); break;
                case 1: TabSkins(); break;
                default: TabThemes(); break;
            }
        }

        void Row(int i, string icon, string title, string desc, string btn, string col, Action act, bool enabled = true, string btnIcon = null, Action tryAct = null)
        {
            var row = UIKit.Panel(shopBody, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -i * rowStep), new Vector2(900, rowH), new Color(0.95f, 0.91f, 0.82f, 1f), "Row");
            UIKit.Icon(row.transform, icon, new Vector2(120, 120), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 0));
            float rightW = tryAct != null ? 270f + 150f + 38f : 270f + 24f;
            bool hasDesc = !string.IsNullOrEmpty(desc);
            var tt = UIKit.Label(row.transform, title, 38, UIKit.Ink, TextAnchor.MiddleLeft, false);
            tt.rectTransform.offsetMin = new Vector2(160, hasDesc ? 30 : 34); tt.rectTransform.offsetMax = new Vector2(-rightW, 0);
            tt.textWrappingMode = TextWrappingModes.NoWrap; tt.overflowMode = TextOverflowModes.Ellipsis;
            if (hasDesc)
            {
                var dd = UIKit.Label(row.transform, desc, 28, new Color(0.35f, 0.3f, 0.5f), TextAnchor.MiddleLeft, false);
                dd.rectTransform.offsetMin = new Vector2(160, -40); dd.rectTransform.offsetMax = new Vector2(-rightW, -40);
            }
            var b = UIKit.Pill(row.transform, btn, enabled ? col : "blue", new Vector2(270, 100), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-24, 0), enabled ? act : null, btnIcon, 36);
            if (!enabled) b.GetComponent<Image>().color = new Color(0.7f, 0.7f, 0.75f, 0.9f);
            if (tryAct != null) UIKit.Pill(row.transform, "TRY", "green", new Vector2(150, 100), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-24 - 270 - 14, 0), tryAct, null, 34);
        }

        bool Spend(int coins)
        {
            if (Save.S.coins < coins) { Toast("Not enough coins"); return false; }
            Save.S.coins -= coins; Save.Commit();
            Sfx.Play("buy");
            return true;
        }

        // the + next to the coin number: 150 free coins for an ad (3 per day, 15 minutes apart)
        void OpenFreeCoins()
        {
            if (modal != null && modal.dim != null) return;
            if (shop != null && shop.dim != null) return;
            Save.CheckAdDay();
            int left = Save.AdCoinsPerDay - Save.S.adCoins;
            int wait = Save.CoinAdWait();
            modal = UIKit.Popup(cv, "FREE COINS", new Vector2(900, 720));
            var c = modal.content;
            UIKit.Icon(c, "coin_pile", new Vector2(300, 250), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -250));
            string lbl = left <= 0 ? "COME BACK TOMORROW" : (wait > 0 ? "WAIT " + (wait / 60).ToString("00") + ":" + (wait % 60).ToString("00") : "+" + Save.AdCoinsAmount + "  AD  (" + left + " left)");
            bool ok = left > 0 && wait <= 0;
            var b = UIKit.Pill(c, lbl, ok ? "orange" : "blue", new Vector2(700, 130), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 190), () =>
            {
                Save.CheckAdDay();
                if (Save.S.adCoins >= Save.AdCoinsPerDay || Save.CoinAdWait() > 0) { Toast("Not ready yet"); return; }
                ShowAd(() => { Save.S.adCoins++; Save.S.adCoinsLastUnix = Save.Now(); Grant(0, 0, 0, Save.AdCoinsAmount); Sfx.Play("coin"); FlyCoins(modal.card.position); modal.Close(); });
            }, "icon_ad", 40);
            if (!ok) b.GetComponent<Image>().color = new Color(0.7f, 0.7f, 0.75f, 0.9f);
            UIKit.Pill(c, "SHOP", "green", new Vector2(700, 110), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 50), () => { modal.Close(); OpenShop(0); }, "icon_shop", 40);
        }

        void Grant(int hint, int undo, int vac, int coins)
        {
            Save.S.hints += hint; Save.S.undos += undo; Save.S.vacuums += vac; Save.S.coins += coins;
            Save.Commit();
        }

        void TabPower()
        {
            Row(0, "icon_undo", "Undo x1", "Bring back the last snake", "100", "blue", () => { if (Spend(100)) Grant(0, 1, 0, 0); }, true, "icon_coin");
            Row(1, "icon_hint", "Hint x1", "Shows the next free snake", "150", "blue", () => { if (Spend(150)) Grant(1, 0, 0, 0); }, true, "icon_coin");
            Row(2, "icon_vacuum", "Vacuum x1", "Remove any one snake", "250", "blue", () => { if (Spend(250)) Grant(0, 0, 1, 0); }, true, "icon_coin");
            Row(3, "icon_gift", "Starter Pack", "3 Hints + 3 Undos + 1 Life", "700", "green", () => { if (Spend(700)) { Grant(3, 3, 0, 0); Save.AddLives(1); } }, true, "icon_coin");
            Row(4, "icon_gift", "Mega Jam Breaker", "5 Hints + 5 Undos + 2 Vacuums", "1500", "green", () => { if (Spend(1500)) Grant(5, 5, 2, 0); }, true, "icon_coin");
            Row(5, "icon_heart", "Refill Lives", "Get all 5 lives back", "400", "green", () => { if (Spend(400)) Save.AddLives(5); }, true, "icon_coin");
            Save.CheckAdDay();
            int left = Save.AdCoinsPerDay - Save.S.adCoins, wait = Save.CoinAdWait();
            bool ready = left > 0 && wait <= 0;
            Row(6, "icon_coin", "Free Coins", left <= 0 ? "Come back tomorrow" : (wait > 0 ? "Next ad in " + (wait / 60) + " min" : "Watch an ad: +" + Save.AdCoinsAmount + " (" + left + " left today)"), ready ? "WATCH" : "WAIT", "orange", () =>
            {
                Save.CheckAdDay();
                if (Save.S.adCoins >= Save.AdCoinsPerDay || Save.CoinAdWait() > 0) { Toast("Not ready yet"); return; }
                ShowAd(() => { Save.S.adCoins++; Save.S.adCoinsLastUnix = Save.Now(); Grant(0, 0, 0, Save.AdCoinsAmount); Sfx.Play("coin"); BuildShopTabs(); });
            }, ready, "icon_ad");
        }

        void TabSkins()
        {
            for (int i = 0; i < Palette.SkinNames.Length; i++)
            {
                int idx = i;
                bool owned = Save.S.ownedSkins.Contains(i);
                bool equipped = Save.S.skin == i;
                string lbl = equipped ? "EQUIPPED" : (owned ? "EQUIP" : Palette.SkinCoinPrice[i].ToString());
                Row(i, "snake_body", Palette.SkinNames[i], "", lbl, owned ? "green" : "purple", () =>
                {
                    if (!Save.S.ownedSkins.Contains(idx)) { if (!Spend(Palette.SkinCoinPrice[idx])) return; Save.S.ownedSkins.Add(idx); }
                    Save.S.skin = idx; Save.Commit(); BuildShopTabs();
                    if (screen == Screen.Play) StartLevel(board.data.level);
                }, !equipped, owned ? null : "icon_coin", owned ? null : (Action)(() => { shop.Close(); OpenPreview(true); }));
                var row = shopBody.GetChild(shopBody.childCount - 1);
                var cols = Palette.Skin(i);
                row.GetChild(0).GetComponent<Image>().color = cols[0];
                for (int k = 0; k < 5; k++)
                    UIKit.Panel(row, new Vector2(0, 0), new Vector2(0, 0), new Vector2(162 + k * 46, 16), new Vector2(38, 38), cols[k + 1], "Dot");
            }
        }

        void TabThemes()
        {
            for (int i = 0; i < Palette.ThemeNames.Length; i++)
            {
                int idx = i;
                bool owned = Save.S.ownedThemes.Contains(i);
                bool equipped = Save.S.theme == i;
                string lbl = equipped ? "IN USE" : (owned ? "USE" : Palette.ThemeCoinPrice[i].ToString());
                Row(i, "icon_star", Palette.ThemeNames[i] + " board", owned ? "Owned" : "Buy with coins", lbl, owned ? "green" : "orange", () =>
                {
                    if (!Save.S.ownedThemes.Contains(idx)) { if (!Spend(Palette.ThemeCoinPrice[idx])) return; Save.S.ownedThemes.Add(idx); }
                    Save.S.theme = idx; Save.Commit(); ApplyTheme(); BuildShopTabs();
                    if (screen == Screen.Play) StartLevel(board.data.level);
                }, !equipped, owned ? null : "icon_coin", owned ? null : (Action)(() => { shop.Close(); OpenPreview(true); }));
                var row = shopBody.GetChild(shopBody.childCount - 1);
                Palette.Theme(i, out _, out var bottom, out _);
                row.GetChild(0).GetComponent<Image>().color = bottom;
            }
        }
    }
}
