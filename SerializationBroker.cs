using SceneSaverBL.Interfaces;
using SLZ.Marrow.Pool;
using SLZ.Marrow.Utilities;
using SLZ.Props;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SceneSaverBL;

internal static class SerializationBroker
{
    public static TSavedObject ConstructObject<TSavedObject, TObjectToBeSaved>(TObjectToBeSaved obj) where TSavedObject : struct, ISavedObject<TSavedObject, TObjectToBeSaved>
    {
        TSavedObject savedObj = default; // epick value type defaulting
        savedObj.Construct(obj);
        return savedObj;
    }

    public static TSavedConstraint ConstructConstraint<TSavedConstraint>(AssetPoolee[] poolees, ConstraintTracker constraint) where TSavedConstraint : struct, ISavedConstraint<TSavedConstraint>
    {
        TSavedConstraint savedObj = default; // epick value type defaulting
        savedObj.Construct(poolees, constraint);
        return savedObj;
    }

    public static ISaveFile CreateSaveAt(string path)
    {
        ISaveFile saveFile = new Versions.Version5.SaveFile5();
        return saveFile;
    }

}
