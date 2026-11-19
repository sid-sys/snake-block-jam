using System;
using System.Collections.Generic;
using UnityEngine;
using GoogleMobileAds.Api;

namespace SnakeJam
{
    // Google AdMob: rewarded and interstitial ads. The editor and development builds use Google's test ads, release builds use the real ones.
    public class Ads : MonoBehaviour
    {
        const string RealInterstitial = "ca-app-pub-2778983812131153/8039011698";
        const string RealRewarded = "ca-app-pub-2778983812131153/1727260929";
        const string TestInterstitial = "ca-app-pub-3940256099942544/1033173712";
        const string TestRewarded = "ca-app-pub-3940256099942544/5224354917";

        static Ads inst;
        static bool Test => Application.isEditor || Debug.isDebugBuild;
        static string InterId => Test ? TestInterstitial : RealInterstitial;
        static string RewardId => Test ? TestRewarded : RealRewarded;

        InterstitialAd inter;
        RewardedAd reward;
        bool ready, loadingInter, loadingReward;
        float nextLoadInter, nextLoadReward;
        readonly Queue<Action> queue = new Queue<Action>();     // ad callbacks can come from another thread: run them here
        public static float LastInterstitialTime = -999f;
        public static int LevelsSinceInterstitial;

        public static bool IsRewardedReady => inst != null && inst.reward != null && inst.reward.CanShowAd();
        public static bool IsInterstitialReady => inst != null && inst.inter != null && inst.inter.CanShowAd();

        public static void Init()
        {
            if (inst != null) return;
#if UNITY_ANDROID || UNITY_IOS
            var go = new GameObject("Ads"); DontDestroyOnLoad(go);
            inst = go.AddComponent<Ads>();
            MobileAds.Initialize(status => inst.Run(() => { inst.ready = true; inst.LoadInter(); inst.LoadReward(); }));
#endif
        }

        void Run(Action a) { lock (queue) queue.Enqueue(a); }

        void Update()
        {
            while (true)
            {
                Action a; lock (queue) { if (queue.Count == 0) break; a = queue.Dequeue(); }
                a?.Invoke();
            }
            if (!ready) return;
            if (inter == null && !loadingInter && Time.unscaledTime > nextLoadInter) LoadInter();
            if (reward == null && !loadingReward && Time.unscaledTime > nextLoadReward) LoadReward();
        }

        void LoadInter()
        {
            loadingInter = true;
            InterstitialAd.Load(InterId, new AdRequest(), (ad, err) => Run(() =>
            {
                loadingInter = false;
                if (err != null || ad == null) { nextLoadInter = Time.unscaledTime + 30f; return; }
                inter = ad;
            }));
        }

        void LoadReward()
        {
            loadingReward = true;
            RewardedAd.Load(RewardId, new AdRequest(), (ad, err) => Run(() =>
            {
                loadingReward = false;
                if (err != null || ad == null) { nextLoadReward = Time.unscaledTime + 30f; return; }
                reward = ad;
            }));
        }

        // onReward runs only if the player watched the whole ad. onFail runs when no ad can be shown.
        public static void ShowRewarded(Action onReward, Action onFail)
        {
            if (inst == null || !IsRewardedReady) { inst?.Run(() => { }); onFail?.Invoke(); return; }
            var ad = inst.reward; inst.reward = null;
            bool earned = false;
            ad.OnAdFullScreenContentClosed += () => inst.Run(() => { if (earned) onReward?.Invoke(); });
            ad.OnAdFullScreenContentFailed += e => inst.Run(() => onFail?.Invoke());
            ad.Show(r => earned = true);
        }

        // shows an interstitial when allowed (every 3rd finished level, and at least 2 minutes apart); next always runs afterwards
        public static void MaybeInterstitial(int level, Action next)
        {
            LevelsSinceInterstitial++;
            bool allowed = level >= 5 && LevelsSinceInterstitial >= 3 && Time.realtimeSinceStartup - LastInterstitialTime >= 120f && IsInterstitialReady;
            if (!allowed) { next?.Invoke(); return; }
            var ad = inst.inter; inst.inter = null;
            LevelsSinceInterstitial = 0; LastInterstitialTime = Time.realtimeSinceStartup;
            ad.OnAdFullScreenContentClosed += () => inst.Run(() => next?.Invoke());
            ad.OnAdFullScreenContentFailed += e => inst.Run(() => next?.Invoke());
            ad.Show();
        }
    }
}
