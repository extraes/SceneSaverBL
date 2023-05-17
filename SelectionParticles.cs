using Jevil;
using PuppetMasta;
using SLZ.AI;
using SLZ.Marrow.Pool;
using SLZ.VFX;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static RootMotion.FinalIK.AimPoser;

namespace SceneSaverBL;

internal readonly struct SelectionChangeData
{
    public readonly Collider collider;
    public readonly int colliderInstanceId;
    public readonly bool removed;

    public bool StillExists => !collider.INOC();

    public SelectionChangeData(Collider col, bool exited)
    {
        colliderInstanceId = col.GetInstanceID();
        removed = exited;
        collider = col;
    }
}

internal static class SelectionParticles
{
    private static readonly Dictionary<int, Renderer[]> pooleeRenderers = new();
    private static readonly Queue<SelectionChangeData> queuedData = new();
    private static readonly Stopwatch timer = new();

    internal static void SetMaterial(int pooleeInstanceId, Material mat) => pooleeRenderers[pooleeInstanceId].ForEach(m => m.sharedMaterial = mat);

    internal static void TriggerEvent(Collider col, bool exited) => queuedData.Enqueue(new(col, exited));

    internal static void WireDisabled()
    {
        pooleeRenderers.ForEach(kvp => { if (!kvp.Value[0].INOC()) GameObject.Destroy(kvp.Value[0].transform.parent.parent.gameObject); } ); // destroys root prefab im p sure
        pooleeRenderers.Clear();
    }
    internal static void WireEnabled() { }
    //internal static void WireEnabled() => pooleeParticles.ForEach(kvp => kvp.Value.enableEmission = true);

    internal static void Thunk()
    {
        timer.Restart();

#if DEBUG
        int collidersChecked = 0;
#endif

        // only take up 2ms MAX per frame, because 1ms might be a bit low, and dont want to take up too much frametime
        while (timer.ElapsedMilliseconds < 2 && queuedData.Count > 0)
        {
#if DEBUG
            collidersChecked++;
#endif
            SelectionChangeData data = queuedData.Dequeue();
            if (!data.StillExists) return;

            Collider col = data.collider;
            AssetPoolee assetPoolee = SceneSaverBL.GetPooleeUpwards(col.transform);

            // modded maps do this weird shit
            if (assetPoolee == null || assetPoolee.spawnableCrate.Barcode.ID == "SLZ.BONELAB.Core.DefaultPlayerRig") return;

            if (data.removed)
            {
                SelectionZone.Instance.pooleesInSelectionZone.Remove(data.colliderInstanceId);
                ParticleCheck(assetPoolee, !SelectionZone.Instance.pooleesInSelectionZone.Any(kvp => kvp.Value == assetPoolee));
            }
            else
            {
                SelectionZone.Instance.pooleesInSelectionZone[data.colliderInstanceId] = assetPoolee;
                ParticleCheck(assetPoolee, false);
            }
        }

#if DEBUG
        if (collidersChecked != 0)
            SceneSaverBL.Log($"Checked {collidersChecked} collider(s). Remaining = {queuedData.Count}");
#endif
    }

    static void ParticleCheck(AssetPoolee poolee, bool removed)
    {
        int pId = poolee.GetInstanceID();

        if (removed && pooleeRenderers.TryGetValue(pId, out Renderer[] renderers))
        {
            pooleeRenderers.Remove(pId);
            // pooling this would be better but i cannot be fucked
            GameObject.Destroy(renderers[0].transform.parent.parent.gameObject);
        }
        else if (!removed && !pooleeRenderers.ContainsKey(pId))
        {
            GameObject savingEffect = GameObject.Instantiate<GameObject>(Assets.Prefabs.SavingObjectBounds.Get());

            Transform parent = poolee.transform;
            AIBrain brain = AIBrain.Cache.Get(poolee.gameObject);
            if (brain != null)
            {
                // set to current location - AIBrain (on root AssetPoolee transform) does not move, but LiteLoco/NavMeshAgent
                BehaviourBaseNav bbn = brain.puppetMaster.behaviours.FirstOrDefault()?.TryCast<BehaviourBaseNav>();
                if (bbn != null)
                    parent = bbn._navAgent.transform;
            }

            savingEffect.transform.SetParent(parent, false);
            savingEffect.transform.localPosition = Vector3.zero;
            savingEffect.transform.localRotation = Quaternion.identity;
            savingEffect.transform.localScale = poolee.spawnableCrate.ColliderBounds.size;

            pooleeRenderers[pId] = savingEffect.GetComponent<LaserVector>().renderers; // yes i did in fact reference smuggle using laservector
            //pooleeParticles[pId] = particles;
            //Renderer rend = poolee.GetComponentInChildren<Renderer>();

            //if (rend.bounds.size.magnitude < 0.1f) rend.ResetBounds(); // if the bounds are (likely) default

            //particleGO.transform.position = rend.bounds.center;
            //particleGO.transform.localScale = rend.bounds.size;
            //particles.emissionRate = Mathf.Clamp(Mathf.Sqrt(rend.bounds.size.magnitude * (8 * 8)), 8, 64);
        }
    }
}
