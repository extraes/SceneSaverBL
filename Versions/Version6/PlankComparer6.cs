namespace SceneSaverBL.Versions.Version6;

internal static class PlankComparer6
{
    static readonly Il2CppSystem.Collections.Generic.List<ConfigurableJoint> Joints = new();

    // the gist: return the highest value between the poolee indexes the plank relies on. its own index doesn't matter. (it does, but I'm just gonna <hope> a plank doesnt get placed in any position past itself lmfao)
    internal static int GetOrderValue(Poolee poolee, Poolee[] allPoolees)
    {
        ObjectDestructible? objDest = Instances<ObjectDestructible>.Get(poolee.transform);

#if DEBUG
        if (objDest == null)
            throw new NullReferenceException($"So-called 'Plank' (barcode {poolee.SpawnableCrate.Barcode.ID}) has no object destructible! It's the liberals! They don't want anything to be destructible.");
#endif

        Joints.Clear();
        poolee.GetComponents(Joints);

#if DEBUG
        SceneSaverBL.Log($"Retrieved {Joints.Count} joint(s) from plank @ {poolee.transform.GetFullPath()}");
        for (int i = 0; i < Joints.Count; i++)
        {
            SceneSaverBL.Log($"    Joints[{i}].connectedBody = {Joints[i].connectedBody?.transform.GetFullPath() ?? "NULL"}");
        }
#endif

        ConfigurableJoint joint1 = Joints[0];
        ConfigurableJoint? joint2 = Joints.Count > 1 ? Joints[1] : null;

        //dummy.Construct(joint1.connectedBody?.transform, new(allPoolees));
        //int idx1 = dummy.IsDummy ? -1 : dummy.pooleeIdx;
        //dummy.Construct(joint2.connectedBody?.transform, new(allPoolees));
        //int idx2 = dummy.IsDummy ? -1 : dummy.pooleeIdx;
        Poolee? p1 = SceneSaverBL.GetPooleeUpwards(joint1.connectedBody?.transform);
        Poolee? p2 = SceneSaverBL.GetPooleeUpwards(joint2?.connectedBody?.transform);

        int idx1 = p1 ? allPoolees.FindIndexOf(p1, UnityObjectComparer<Poolee?>.Instance) : 0;
        int idx2 = p2 ? allPoolees.FindIndexOf(p2, UnityObjectComparer<Poolee?>.Instance) : 0;

#if DEBUG
        //if (allPoolees.Contains())

        SceneSaverBL.Log($"Plank @ idx {allPoolees.FindIndexOf(poolee)} connects to poolees @ idxs {idx1} & {idx2}");
#endif
        return Math.Max(idx1, idx2);
    }
}
