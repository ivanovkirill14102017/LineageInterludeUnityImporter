using System;
using System.IO;
using L2Viewer.SceneDomain.Services;
using L2Viewer.SceneDomain.Models;
using UnityEngine;

internal static class SharedSkyTimeProfileImporter
{
    internal static void ImportFromConfiguredClient()
    {
        var clientRoot = ConstInfo.L2GameClientPath;
        if (string.IsNullOrWhiteSpace(clientRoot) || !Directory.Exists(clientRoot))
            throw new DirectoryNotFoundException($"Client root was not found: '{clientRoot}'.");

        var source = new SceneSkyTimeEnvironmentBuilder().Build(clientRoot);
        var profiles = new SkyTimeProfileData.Profile[source.Profiles.Length];
        for (var i = 0; i < profiles.Length; i++)
        {
            var sourceProfile = source.Profiles[i];
            var profile = new SkyTimeProfileData.Profile { EnvironmentType = sourceProfile.EnvironmentType };
            foreach (var track in sourceProfile.Tracks)
            {
                var target = ResolveTrack(profile, track.Role);
                target.Keys = new SkyTimeProfileData.ColorKey[track.Keys.Length];
                for (var keyIndex = 0; keyIndex < track.Keys.Length; keyIndex++)
                {
                    var key = track.Keys[keyIndex];
                    target.Keys[keyIndex] = new SkyTimeProfileData.ColorKey
                    {
                        Hour = key.Hour,
                        Color = new Color32(key.Red, key.Green, key.Blue, 255)
                    };
                }
            }
            profiles[i] = profile;
        }

        var timeProfiles = new SkyTimeProfileData { Profiles = profiles };
        timeProfiles.Validate();
        SharedSkySceneImporter.Build(clientRoot, source, timeProfiles);
    }

    private static SkyTimeProfileData.ColorTrack ResolveTrack(
        SkyTimeProfileData.Profile profile, SceneSkyTimeLayerRole role) => role switch
    {
        SceneSkyTimeLayerRole.SkyBox => profile.SkyBox,
        SceneSkyTimeLayerRole.HazeRing => profile.HazeRing,
        SceneSkyTimeLayerRole.Cloud1 => profile.Cloud1,
        SceneSkyTimeLayerRole.Cloud2 => profile.Cloud2,
        SceneSkyTimeLayerRole.Cloud3 => profile.Cloud3,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
    };

}
