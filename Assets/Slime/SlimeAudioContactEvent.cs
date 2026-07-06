using UnityEngine;

namespace Slime
{
    [RequireComponent(typeof(AudioSource))]
    [AddComponentMenu("Slime/Events/Audio Contact Event")]
    public sealed class SlimeAudioContactEvent : SlimeContactEventHandler
    {
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip contactBeganClip;
        [SerializeField, Range(0f, 1f)] float contactBeganVolume = 1f;
        [SerializeField] AudioClip contactEndedClip;
        [SerializeField, Range(0f, 1f)] float contactEndedVolume = 1f;
        [SerializeField] SlimeContactEventTriggerMode triggerMode = SlimeContactEventTriggerMode.RepeatableStart;
        [SerializeField, Min(0f)] float minPlayInterval = 0.05f;
        [SerializeField] bool useUnscaledTime;

        bool _hasTriggered;
        float _lastPlayTime = -999f;

        public bool HasTriggered => _hasTriggered;

        void Awake()
        {
            ResolveAudioSource();
        }

        void OnValidate()
        {
            contactBeganVolume = Mathf.Clamp01(contactBeganVolume);
            contactEndedVolume = Mathf.Clamp01(contactEndedVolume);
            minPlayInterval = Mathf.Max(0f, minPlayInterval);
        }

        public override void HandleContactBegan(SlimeContactEventContext context)
        {
            if (!CanStartContactEvent(triggerMode, _hasTriggered))
                return;

            if (TryPlay(contactBeganClip, contactBeganVolume))
                _hasTriggered = true;
        }

        public override void HandleContactEnded(SlimeContactEventContext context)
        {
            TryPlay(contactEndedClip, contactEndedVolume);
        }

        public void ResetAudioContactState()
        {
            _hasTriggered = false;
            _lastPlayTime = -999f;
        }

        bool TryPlay(AudioClip clip, float volume)
        {
            if (clip == null || volume <= 0f)
                return false;

            float now = useUnscaledTime ? Time.unscaledTime : Time.time;
            if (now - _lastPlayTime < minPlayInterval)
                return false;

            AudioSource source = ResolveAudioSource();
            if (source == null)
                return false;

            source.PlayOneShot(clip, volume);
            _lastPlayTime = now;
            return true;
        }

        AudioSource ResolveAudioSource()
        {
            if (audioSource != null)
                return audioSource;

            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();

            audioSource.playOnAwake = false;
            return audioSource;
        }
    }
}
