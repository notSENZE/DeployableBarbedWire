using Comfort.Common;
using EFT.CameraControl;
using UnityEngine;

namespace DeployableBarbedWire.Client.BarbedWire;

internal sealed class DeployableBarbedWireDamage : EFT.Interactive.BarbedWire
{
    private const float SoundInterval = 0.5f;
    private const float SoundVolume = 1.8f;

    private Collider _triggerCollider;
    private float _lastSoundTime = float.NegativeInfinity;

    private void OnEnable()
    {
        _triggerCollider = GetComponent<Collider>();
    }

    private void OnTriggerStay(Collider enteredCollider)
    {
        if (_triggerCollider != null)
        {
            ((IPhysicsTriggerWithStay)this).OnTriggerStay(enteredCollider, _triggerCollider);
        }
    }

    public override void PlaySound(bool useOcclusion)
    {
        if (_soundBank == null
            || Time.time - _lastSoundTime < SoundInterval
            || !Singleton<BetterAudio>.Instantiated
            || !CameraManager.Exist)
        {
            return;
        }

        var position = transform.position;
        Singleton<BetterAudio>.Instance.PlayAtPoint(
            position,
            _soundBank,
            CameraManager.Instance.Distance(position),
            SoundVolume,
            -1f,
            EnvironmentType.Outdoor,
            useOcclusion ? EOcclusionTest.OneShotPropagation : EOcclusionTest.None,
            false,
            false);
        _lastSoundTime = Time.time;
    }
}
