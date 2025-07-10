using Jevil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using Il2CppSLZ.Marrow.Warehouse;
using Il2CppSLZ.Marrow.Pool;
using BoneLib.Notifications;
using SceneSaverBL.Exceptions;
using System.Diagnostics;
using LabFusion.RPC;
using LabFusion.Network;

namespace SceneSaverBL;

internal static class Spawnington
{
    static readonly bool FusionLoaded = Utilities.IsFusionLoaded();
    static readonly PriorityQueue<Poolee, float> pooleeDistanceRanks = new();
    static readonly CircularBuffer<(string, Poolee)> latestPooleeSpawns = new(1024);
    [HarmonyPatch(typeof(Poolee), nameof(Poolee.OnEnable))]
    public static class Patchy
    {
        public static void Postfix(Poolee __instance)
        {
            if (__instance.SpawnableCrate?.Barcode is null)
                return;

            latestPooleeSpawns.PushBack((__instance.SpawnableCrate.Barcode.ID, __instance));
#if DEBUG
            SceneSaverBL.Log($"Poolee {__instance} spawned, at {__instance.transform.position} & w/ rot {__instance.transform.rotation}");
#endif
        }
    }

    // SpawnAsyncS, S for SSBL
    public static async Task<Poolee?> SpawnAsyncS(this SpawnableCrate crate, Vector3 pos, Quaternion rot)
    {
        Spawnable spawn = Barcodes.ToSpawnable(crate.Barcode.ID);
        return await SpawnAsyncS(spawn, pos, rot);
    }

    public static async Task<Poolee?> SpawnAsyncS(this Spawnable spawnable, Vector3 pos, Quaternion rot)
    {
        if (FusionLoaded && Prefs.fusionSync)
            return await SpawnFromFusion(spawnable, pos, rot);
        else
            return await SpawnFromBasegame(spawnable, pos, rot);
    }

    private static async Task<Poolee?> SpawnFromBasegame(Spawnable spawnable, Vector3 pos, Quaternion rot)
    {
        Poolee pooleeFromSpawner = await AssetSpawner.SpawnAsync(spawnable, pos, rot, Utilities.NulledNullable<Vector3>(), null, false, Utilities.NulledNullable<int>());
        if (pooleeFromSpawner != null)
        {
#if DEBUG
            SceneSaverBL.Log("UniTasks/SpawnAsync worked as expected and returned a value! Bad code avoided!");
#endif
            return pooleeFromSpawner;
        }

        (string barcodeId, Poolee pooleeFromBuffer) = latestPooleeSpawns.Back();
        if (barcodeId == spawnable.crateRef.Barcode.ID && pooleeFromBuffer.transform.position == pos && pooleeFromBuffer.transform.rotation == rot)
        {
#if DEBUG
            SceneSaverBL.Log($"Hit fast-track for {pooleeFromBuffer.name} from crate {spawnable.crateRef.Barcode.ID}!");
#endif
            latestPooleeSpawns.PopBack();
            return pooleeFromBuffer;
        }


        pooleeDistanceRanks.Clear();
        foreach (var (id, poolee) in latestPooleeSpawns)
        {
            if (id == spawnable.crateRef.Barcode.ID)
            {
                pooleeDistanceRanks.Enqueue(poolee, Vector3.Distance(poolee.transform.position, pos));
#if DEBUG
                SceneSaverBL.Log($"Found poolee {poolee.name} for crate {spawnable.crateRef.Barcode.ID} @ pos {poolee.transform.position} ({Vector3.Distance(poolee.transform.position, pos)} meters away) & rot ({Quaternion.Angle(poolee.transform.rotation, rot)} deg away)");
#endif
            }
        }

        if (pooleeDistanceRanks.Count == 0)
        {
            SceneSaverBL.Warn($"Attempted to spawn something with the barcode {spawnable.crateRef.Barcode.ID} but there was nothing with the barcode found in the recent spawnable list! This may be the result of a cold-start, so try again");
            //var notif = new Notification()
            //{
            //    Message = new NotificationText("There was an issue. Please try again."),
            //    Type = NotificationType.Warning,
            //    Title = "SceneSaverBL",
            //};
            //Notifier.Send(notif);
            return null;
        }

#if DEBUG
        // this code is so ass but SpawnAsync isnt doing fucking SHIT
        SceneSaverBL.Log($"Returning poolee @ pos {pooleeDistanceRanks.Peek().transform.position} & rot {pooleeDistanceRanks.Peek().transform.rotation}");
#endif
        return pooleeDistanceRanks.Dequeue();
    }

    static async Task<Poolee?> SpawnFromFusion(Spawnable spawnable, Vector3 pos, Quaternion rot)
    {
        // Don't move into SpawnAsyncS -- NetworkInfo can't be referenced in a method that gets used when fusion isn't loaded
        if (!NetworkInfo.HasServer)
        {
#if DEBUG
            SceneSaverBL.Log("Fusion was loaded, but not connected to a server. Falling back to basegame spawning! Peep this #ShitCode!!!");
#endif
            return await SpawnFromBasegame(spawnable, pos, rot);
        }

#if DEBUG
        string barcode = spawnable.crateRef?.Barcode?.ID ?? "<null barcode>";
        SceneSaverBL.Log($"Spawning a {barcode} with fusion!");
#endif

        GameObject? spawnedObject = null;
        var info = new NetworkAssetSpawner.SpawnRequestInfo()
        {
            Spawnable = spawnable,
            Position = pos,
            Rotation = rot,
            SpawnCallback = (info) =>
            {
                spawnedObject = info.Spawned;
            }
        };

        NetworkAssetSpawner.Spawn(info);

        while (spawnedObject is null)
            await UniTask.Yield();


#if DEBUG
        //Utilities.InspectInUnityExplorer(spawnedObject);

        SceneSaverBL.Log($"Spawned object with fusion : {spawnedObject?.transform?.GetFullPath() ?? "null"} (Barcode {barcode})");
#endif

        return spawnedObject == null ? null : Instances<Poolee>.Get(spawnedObject);
    }
}
