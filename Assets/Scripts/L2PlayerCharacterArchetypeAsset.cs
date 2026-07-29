using System;
using L2Viewer.SceneDomain.Models;
using UnityEngine;

[CreateAssetMenu(menuName = "L2/Player Character Archetype", fileName = "L2PlayerCharacterArchetype")]
public sealed class L2PlayerCharacterArchetypeAsset : L2ModularCharacterArchetypeAssetBase
{
    public string BaseClass;
    public string Gender;
    public string VisualFamily;
}
