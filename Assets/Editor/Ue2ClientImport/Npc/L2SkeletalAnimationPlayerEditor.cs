using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(L2SkeletalAnimationPlayer))]
public sealed class L2SkeletalAnimationPlayerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var player = (L2SkeletalAnimationPlayer)target;

        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkeletalAnimationPlayer.Asset)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkeletalAnimationPlayer.PoseMode)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkeletalAnimationPlayer.PreviewInEditMode)));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Visibility", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkeletalAnimationPlayer.ShowMesh)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkeletalAnimationPlayer.ShowSkeleton)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkeletalAnimationPlayer.DrawBoneLabels)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkeletalAnimationPlayer.BoneMarkerSize)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkeletalAnimationPlayer.BoneAxisLength)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkeletalAnimationPlayer.BoneColor)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkeletalAnimationPlayer.BoneAxisColor)));

        var names = player.GetAnimationNames();
        var hasAnimations = names.Length > 0;
        if (names.Length > 0)
        {
            var selected = Mathf.Clamp(player.SelectedAnimationIndex, 0, names.Length - 1);
            var nextSelected = EditorGUILayout.Popup("Selected Animation", selected, names);
            if (nextSelected != selected)
            {
                Undo.RecordObject(player, "Change animation");
                player.SetSelectedAnimation(nextSelected, true);
                EditorUtility.SetDirty(player);
            }
        }
        else
        {
            EditorGUILayout.HelpBox("No animations found on the imported asset.", MessageType.Info);
        }

        using (new EditorGUI.DisabledScope(player.Asset == null))
        {
            EditorGUILayout.HelpBox(
                "This player now samples the same shared SceneDomain skeletal session as WPF using a single bone-skinning deformation path.",
                MessageType.None);

            if (hasAnimations)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkeletalAnimationPlayer.PlayOnEnable)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkeletalAnimationPlayer.Loop)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkeletalAnimationPlayer.PlaybackSpeed)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkeletalAnimationPlayer.IsPlaying)));

                EditorGUI.BeginChangeCheck();
                var duration = Mathf.Max(0.001f, player.GetSelectedAnimationDuration());
                var previewTime = EditorGUILayout.Slider("Preview Time", player.PreviewTimeSeconds, 0f, duration);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(player, "Change preview time");
                    player.PreviewTimeSeconds = previewTime;
                    player.SampleNow();
                    EditorUtility.SetDirty(player);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Mesh Only"))
                {
                    Undo.RecordObject(player, "Show mesh only");
                    player.SetVisibility(true, false);
                    EditorUtility.SetDirty(player);
                }

                if (GUILayout.Button("Skeleton Only"))
                {
                    Undo.RecordObject(player, "Show skeleton only");
                    player.SetVisibility(false, true);
                    EditorUtility.SetDirty(player);
                }

                if (GUILayout.Button("Mesh + Skeleton"))
                {
                    Undo.RecordObject(player, "Show mesh and skeleton");
                    player.SetVisibility(true, true);
                    EditorUtility.SetDirty(player);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Static Mesh"))
                {
                    Undo.RecordObject(player, "Sample static mesh");
                    player.SampleStaticMesh();
                    EditorUtility.SetDirty(player);
                }

                if (GUILayout.Button("Bind Pose"))
                {
                    Undo.RecordObject(player, "Sample bind pose");
                    player.SampleBindPose();
                    EditorUtility.SetDirty(player);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!hasAnimations))
                {
                    if (GUILayout.Button("Animated"))
                    {
                        Undo.RecordObject(player, "Set animated mode");
                        player.SetPoseMode(L2SkeletalAnimationPlayer.DiagnosticPoseMode.Animated, false);
                        player.SampleNow();
                        EditorUtility.SetDirty(player);
                    }

                    if (GUILayout.Button("No Interp"))
                    {
                        Undo.RecordObject(player, "Set no interpolation mode");
                        player.SetPoseMode(L2SkeletalAnimationPlayer.DiagnosticPoseMode.AnimatedWithoutInterpolation, false);
                        player.SampleNow();
                        EditorUtility.SetDirty(player);
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!hasAnimations))
                {
                    if (GUILayout.Button(player.IsPlaying ? "Pause" : "Play"))
                    {
                        Undo.RecordObject(player, "Toggle animation playback");
                        if (player.IsPlaying)
                        {
                            player.Pause();
                        }
                        else
                        {
                            player.PlaySelected();
                        }

                        EditorUtility.SetDirty(player);
                    }
                }

                if (GUILayout.Button("Sample Now"))
                {
                    player.SampleNow();
                    EditorUtility.SetDirty(player);
                }
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}
