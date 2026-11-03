using System;
using System.IO;
using UnityEngine;

public enum SkyLayerRole
{
    SkyBox,
    HazeRing,
    Cloud1,
    StarsLarge,
    StarsSmall
}

[Serializable]
public sealed class SkyTimeProfileData
{
    [Serializable]
    public struct ColorKey
    {
        public float Hour;
        public Color Color;
    }

    [Serializable]
    public sealed class ColorTrack
    {
        public ColorKey[] Keys = Array.Empty<ColorKey>();

        public Color Evaluate(float hour)
        {
            if (Keys == null || Keys.Length == 0)
                throw new InvalidDataException("Sky color track has no keys.");
            if (Keys.Length == 1)
                return Keys[0].Color;

            hour = Mathf.Repeat(hour, 24f);
            if (hour < Keys[0].Hour)
            {
                var previousDayLast = Keys[Keys.Length - 1];
                return Color.Lerp(previousDayLast.Color, Keys[0].Color,
                    Mathf.InverseLerp(previousDayLast.Hour - 24f, Keys[0].Hour, hour));
            }
            for (var i = 1; i < Keys.Length; i++)
            {
                if (hour <= Keys[i].Hour)
                {
                    var start = Keys[i - 1];
                    var end = Keys[i];
                    return Color.Lerp(start.Color, end.Color,
                        Mathf.InverseLerp(start.Hour, end.Hour, hour));
                }
            }

            var last = Keys[Keys.Length - 1];
            var first = Keys[0];
            var endHour = first.Hour + 24f;
            return Color.Lerp(last.Color, first.Color,
                Mathf.InverseLerp(last.Hour, endHour, hour));
        }
    }

    [Serializable]
    public sealed class Profile
    {
        public int EnvironmentType;
        public ColorTrack SkyBox = new ColorTrack();
        public ColorTrack HazeRing = new ColorTrack();
        public ColorTrack Cloud1 = new ColorTrack();
        public ColorTrack Cloud2 = new ColorTrack();
        public ColorTrack Cloud3 = new ColorTrack();

        public Color Evaluate(SkyLayerRole role, float hour)
        {
            return role switch
            {
                SkyLayerRole.SkyBox => SkyBox.Evaluate(hour),
                SkyLayerRole.HazeRing => HazeRing.Evaluate(hour),
                SkyLayerRole.Cloud1 => Cloud1.Evaluate(hour),
                SkyLayerRole.StarsLarge => Cloud2.Evaluate(hour),
                SkyLayerRole.StarsSmall => Cloud3.Evaluate(hour),
                _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
            };
        }
    }

    public Profile[] Profiles = Array.Empty<Profile>();

    public void Validate()
    {
        if (Profiles == null || Profiles.Length == 0)
            throw new InvalidDataException("Sky time profiles are missing.");
        var types = new System.Collections.Generic.HashSet<int>();
        foreach (var profile in Profiles)
        {
            if (profile == null || !types.Add(profile.EnvironmentType))
                throw new InvalidDataException("Sky time profiles contain a null or duplicate environment type.");
            ValidateTrack(profile.SkyBox, "SkyBox");
            ValidateTrack(profile.HazeRing, "HazeRing");
            ValidateTrack(profile.Cloud1, "Cloud1");
            ValidateTrack(profile.Cloud2, "Cloud2");
            ValidateTrack(profile.Cloud3, "Cloud3");
        }
    }

    private static void ValidateTrack(ColorTrack track, string name)
    {
        if (track?.Keys == null || track.Keys.Length == 0)
            throw new InvalidDataException($"Sky color track {name} is missing.");
        for (var i = 0; i < track.Keys.Length; i++)
        {
            var hour = track.Keys[i].Hour;
            if (float.IsNaN(hour) || hour < 0f || hour > 24f ||
                (i > 0 && hour <= track.Keys[i - 1].Hour))
                throw new InvalidDataException($"Sky color track {name} has an invalid key time.");
        }
    }

    public Profile GetProfile(int environmentType)
    {
        if (Profiles == null || Profiles.Length == 0)
            throw new InvalidDataException("Sky time profiles are missing.");
        foreach (var profile in Profiles)
        {
            if (profile == null)
                throw new InvalidDataException("Sky time profile is null.");
            if (profile.EnvironmentType == environmentType)
                return profile;
        }

        throw new ArgumentOutOfRangeException(nameof(environmentType), environmentType,
            "The requested sky environment profile was not imported.");
    }
}
