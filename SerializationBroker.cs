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
    public static TSavedObject ConstructObject<TSavedObject, TObjectToBeSaved, TContext>(TObjectToBeSaved obj, TContext context) 
        where TSavedObject : struct, ISavedObject<TSavedObject, TObjectToBeSaved, TContext>
    {
        TSavedObject savedObj = default; // epick value type defaulting
        savedObj.Construct(context, obj);
        return savedObj;
    }

    public static TSavedConstraint ConstructConstraint<TSavedConstraint, TContext>(AssetPoolee[] poolees, ConstraintTracker constraint, TContext context)
        where TSavedConstraint : struct, ISavedConstraint<TSavedConstraint, TContext>
    {
        TSavedConstraint savedObj = default; // epick value type defaulting
        savedObj.Construct(context, poolees, constraint);
        return savedObj;
    }

    public static ISaveFile CreateSaveAt(string path)
    {
        ISaveFile saveFile = new Versions.Version6.SaveFile6();
        return saveFile;
    }

}
