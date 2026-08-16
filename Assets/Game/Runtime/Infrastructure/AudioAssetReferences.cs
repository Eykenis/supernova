using Supernova.Audio;
using UnityEngine;

namespace Supernova.Infrastructure
{
    [System.Serializable]
    public sealed class AudioAssetReferences
    {
        [SerializeField] private SoundEffectCue coinDeposit;
        [SerializeField] private SoundEffectCue caveAmbience;
        [SerializeField] private SoundEffectCue cashGrowing;
        [SerializeField] private SoundEffectCue missionStart;
        [SerializeField] private SoundEffectCue missionReady;
        [SerializeField] private SoundEffectCue creatureRun;
        [SerializeField] private SoundEffectCue creatureAttack;
        [SerializeField] private SoundEffectCue creatureHitPlayer;
        [SerializeField] private SoundEffectCue playerFallSmall;
        [SerializeField] private SoundEffectCue playerFallBig;
        [SerializeField] private SoundEffectCue bombFuse;
        [SerializeField] private SoundEffectCue bombExplosion;

        public SoundEffectCue CoinDeposit => coinDeposit;
        public SoundEffectCue CaveAmbience => caveAmbience;
        public SoundEffectCue CashGrowing => cashGrowing;
        public SoundEffectCue MissionStart => missionStart;
        public SoundEffectCue MissionReady => missionReady;
        public SoundEffectCue CreatureRun => creatureRun;
        public SoundEffectCue CreatureAttack => creatureAttack;
        public SoundEffectCue CreatureHitPlayer => creatureHitPlayer;
        public SoundEffectCue PlayerFallSmall => playerFallSmall;
        public SoundEffectCue PlayerFallBig => playerFallBig;
        public SoundEffectCue BombFuse => bombFuse;
        public SoundEffectCue BombExplosion => bombExplosion;
        public bool IsComplete =>
            coinDeposit != null
            && caveAmbience != null
            && cashGrowing != null
            && missionStart != null
            && missionReady != null
            && creatureRun != null
            && creatureAttack != null
            && creatureHitPlayer != null
            && playerFallSmall != null
            && playerFallBig != null
            && bombFuse != null
            && bombExplosion != null;
    }
}
