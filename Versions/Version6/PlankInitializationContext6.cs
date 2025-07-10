using Il2CppSLZ.Bonelab;

namespace SceneSaverBL.Versions.Version6;

internal struct PlankInitializationContext6
{
    // only used for deserialization
    internal BoardGenerator boardGun;
    internal Poolee[] poolees;
    internal Vector3 worldspaceOffset;

    // used only for serialization
    //internal Transform root;
}
