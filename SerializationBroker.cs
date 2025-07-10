using SceneSaverBL.Interfaces;

namespace SceneSaverBL;

internal static class SerializationBroker
{
    public static TSavedObject ConstructObject<TSavedObject, TObjectToBeSaved>(TObjectToBeSaved obj) where TSavedObject : struct, ISavedObject<TSavedObject, TObjectToBeSaved>
    {
        TSavedObject savedObj = default; // epick value type defaulting
        savedObj.Construct(obj);
        return savedObj;
    }

    public static TSavedConstraint ConstructConstraint<TSavedConstraint>(Poolee[] poolees, ConstraintTracker constraint) where TSavedConstraint : struct, ISavedConstraint<TSavedConstraint>
    {
        TSavedConstraint savedObj = default; // epick value type defaulting
        savedObj.Construct(poolees, constraint);
        return savedObj;
    }

    public static ISaveFile CreateSaveAt(string path)
    {
        ISaveFile saveFile = new Versions.Version6.SaveFile6();
        return saveFile;
    }
}
