namespace SceneSaverBL.Interfaces;

internal interface ISavedConstraint<TSavedConstraint> : IEquatable<TSavedConstraint>, ISerializableStruct<TSavedConstraint> where TSavedConstraint : struct, ISavedConstraint<TSavedConstraint>
{
    public (int, int) DependentOn { get; }

    public void Construct(Poolee[] poolees, ConstraintTracker constraint);
    public void Initialize(Poolee[] poolees, Constrainer constrainer);
}
