using Cysharp.Threading.Tasks;
using Jevil;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SceneSaverBL;

internal static class Assets
{
    private static AssetBundle bundle;

    internal static class Prefabs
    {
        internal static readonly BundledAsset<GameObject> SelectionWire = new("Assets/SceneSaver/SelectionParticlePrefab.prefab");
        internal static readonly BundledAsset<GameObject> SelectionWireMusic = new("Assets/SceneSaver/SelectionWireMusic.prefab");
        internal static readonly BundledAsset<GameObject> SavingObjectBounds = new("Assets/SceneSaver/SavingBounds.prefab");
        internal static readonly BundledAsset<GameObject> SavingPhotographer = new("Assets/SceneSaver/Photographer.prefab");
        internal static readonly BundledAsset<GameObject> IdlePhotographer = new("Assets/SceneSaver/PhotographerIdle.prefab");
        internal static readonly BundledAsset<GameObject> CameraFlash = new("Assets/SceneSaver/CameraParticles.prefab");
        internal static readonly BundledAsset<GameObject> Polaroid = new("Assets/SceneSaver/PreviewPolaroid.prefab");
        internal static readonly BundledAsset<GameObject> FullsavePreviewBounds = new("Assets/SceneSaver/FullsaveBounds.prefab");
        internal static readonly BundledAsset<GameObject> ControllerTutorial = new("Assets/SceneSaver/Tutorial/ControllerTutorial.prefab");
    }
    
    internal static class Materials
    {
        internal static readonly BundledAsset<Material> SavingObjectCompletedMaterial = new("Assets/SceneSaver/BoundsLinesSAVED.mat");
    }

    internal static async Task Init()
    {
#if DEBUG
        Stopwatch sw = Stopwatch.StartNew();
        SceneSaverBL.Log("Loading assets...");
#endif
        string resourcePath = "SceneSaverBL.Resources.Resources" + (Utilities.IsPlatformQuest() ? "Quest.bundle" : ".bundle");
        byte[] bundleBytes = null;
        SceneSaverBL.instance.MelonAssembly.Assembly.UseEmbeddedResource(resourcePath, bytes => bundleBytes = bytes);

        bundle = await AssetBundle.LoadFromMemoryAsync(bundleBytes).ToTask();
        bundle.Persist();

#if DEBUG
        SceneSaverBL.Log($"Loaded resource bundle in {sw.ElapsedMilliseconds} ms.");
        sw.Restart();
#endif
        
        await Prefabs.SelectionWire.BindAsync(bundle);
        await Prefabs.SelectionWireMusic.BindAsync(bundle);
        await Prefabs.SavingObjectBounds.BindAsync(bundle);
        await Prefabs.SavingPhotographer.BindAsync(bundle);
        await Prefabs.IdlePhotographer.BindAsync(bundle);
        await Prefabs.CameraFlash.BindAsync(bundle);
        await Prefabs.Polaroid.BindAsync(bundle);
        await Prefabs.FullsavePreviewBounds.BindAsync(bundle);
        await Prefabs.ControllerTutorial.BindAsync(bundle);

        await Materials.SavingObjectCompletedMaterial.BindAsync(bundle);

#if DEBUG
        SceneSaverBL.Log($"Loaded resources from bundle in {sw.ElapsedMilliseconds} ms.");
#endif
    }
}
