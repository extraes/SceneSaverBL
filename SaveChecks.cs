using Jevil;
using SceneSaverBL.Exceptions;
using SLZ.Marrow.Pool;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SceneSaverBL;

internal static class SaveChecks
{
    // perform no operations on these strings so they dont alloc extra memory
    const string POOLED_RIG_MANAGER = "SLZ.BONELAB.Core.DefaultPlayerRig";
    const string CONSTRAINT_NAME_START = "jPt";
    const string SAVING_BOUNDS_NAME = "SavingBounds";

    static readonly Dictionary<string, bool> HierarchyMatchCache = new();
    static int mainThreadId;

    public static bool CanBeSerializedDeserialized(string barcode)
    {
        return barcode != POOLED_RIG_MANAGER;
    }
    
    public static bool IsTransformIgnored(Transform transformName)
    {
        string tName = transformName.name;
        return tName.StartsWith(CONSTRAINT_NAME_START) || tName.StartsWith(SAVING_BOUNDS_NAME);
    }

    public static bool IsHierarchyConsistent(AssetPoolee poolee)
    {
#if DEBUG
        using var ps = new ProfilingScope(SceneSaverBL.instance.LoggerInstance, ProfilingScope.ProfilingType.STOPWATCH_EXECUTION_TIME, "Hierarchy consistency");
#endif
        string barcode = poolee.spawnableCrate.Barcode.ID;

        if (HierarchyMatchCache.TryGetValue(barcode, out bool cachedConsistent))
            return cachedConsistent;

        Hash128 hierarchyHashPrefab = HierarchyHash(poolee.spawnableCrate.MainGameObject.Asset.transform);

#if DEBUG
        ps.Log();
#endif

        Hash128 hierarchyHashInstance = HierarchyHash(poolee.transform);

        bool retVal = hierarchyHashInstance.Equals(hierarchyHashPrefab);
        HierarchyMatchCache[barcode] = retVal;
        return retVal;
    }

    // this definitely isnt a foolproof way of checking a transform's "hierarchy hash", but i think its definitely faster than using shit like name string hashing or GetComponentInChildren'ing
    static Hash128 HierarchyHash(Transform t, Hash128 continueHash = default)
    {
        //if (t.childCount == 0) return continueHash;

        int ignoreCount = 0;

        for (int i = 0; i < t.childCount; i++)
        {
            Transform tChild = t.GetChild(i);
            
            // the name checking will surely increase save times EXPONENTIALLY
            if (IsTransformIgnored(tChild))
            {
                ignoreCount++;
                continue;
            }
            continueHash = HierarchyHash(tChild, continueHash);
        }

        continueHash.Append(t.childCount - ignoreCount);

        return continueHash;
    }

    internal static void ThrowIfDefault(int checkFor, [CallerArgumentExpression("checkFor")] string name = default)
    {
        if (checkFor == default) LogThrow(new UninitializedSerializedDataException($"The int variable {name} must be assigned a value instead of its default value."));
    }

    internal static void ThrowIfDefault(Vector3 checkFor, [CallerArgumentExpression("checkFor")] string name = default)
    {
        if (checkFor == default) LogThrow(new UninitializedSerializedDataException($"The Vector3 variable {name} must be assigned a value instead of its default value."));
    }

    internal static void ThrowIfDefault<T>(T checkFor, [CallerArgumentExpression("checkFor")] string name = default) where T : IEquatable<T>
    {
        if (checkFor.Equals(default)) LogThrow(new UninitializedSerializedDataException($"The equatable variable {name} must be assigned a value instead of its default value."));
    }

    internal static void ThrowIfDefault(object obj, [CallerArgumentExpression("obj")] string name = default)
    {
        if (obj is null) LogThrow(new UninitializedSerializedDataException($"The reference variable {name} must be assigned a value instead of null."));
    }

    internal static void ThrowIfInvalid(Vector3 vec, [CallerArgumentExpression("vec")] string name = default)
    {
        bool isNan = float.IsNaN(vec.x)
                  || float.IsNaN(vec.y)
                  || float.IsNaN(vec.z);
        bool isInf = float.IsInfinity(vec.x)
                  || float.IsInfinity(vec.y)
                  || float.IsInfinity(vec.z);
        if (isNan || isInf) LogThrow(new InvalidDataException($"Invalid {name} Vector3: {SaveUtils.ToStr(vec)}"));
    }

    internal static void EstablishMainThread()
    {
        if (mainThreadId != default) SceneSaverBL.Warn($"Main thread ID is already set to {mainThreadId}! Now setting to {Thread.CurrentThread.ManagedThreadId}");
        mainThreadId = Thread.CurrentThread.ManagedThreadId;
    }

    internal static void ThrowIfOffMainThread([CallerMemberName] string caller = default, [CallerLineNumber] int lineNum = default)
    {
        if (Thread.CurrentThread.ManagedThreadId != mainThreadId) LogThrow(new ThreadStateException($"Execution was knocked off the main thread in {caller}! See line {lineNum}."));
    }

    internal static void ThrowIfLongerThanByte(string strToCheck, Encoding encoder = default)
    {
        encoder ??= Encoding.UTF8;
        int byteCount = encoder.GetByteCount(strToCheck);
        if (byteCount > byte.MaxValue) LogThrow(new StringTooLongException($"String is too long to be serialized ({byteCount} is greater than max 255 characters). Report this error to the developer (or maybe shorten barcode)! String: " + strToCheck));
    }

    private static void LogThrow(Exception ex)
    {
        SceneSaverBL.Error(ex);
        throw ex;
    }
}
