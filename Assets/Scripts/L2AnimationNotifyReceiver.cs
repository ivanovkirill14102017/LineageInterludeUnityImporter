using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class L2AnimationNotifyReceiver : MonoBehaviour
{
    [SerializeField] private string _lastNotifyPayload;

    public string LastNotifyPayload => _lastNotifyPayload;

    public event Action<string> NotifyReceived;

    public void OnL2AnimationNotify(string payload)
    {
        _lastNotifyPayload = payload ?? string.Empty;
        NotifyReceived?.Invoke(_lastNotifyPayload);
    }
}
