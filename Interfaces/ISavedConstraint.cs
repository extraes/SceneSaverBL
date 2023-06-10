using SLZ.Marrow.Pool;
using SLZ.Props;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SceneSaverBL.Interfaces;

internal interface ISavedConstraint<TSavedConstraint, TConstraintContext> : IEquatable<TSavedConstraint>, ISerializableStruct<TSavedConstraint>, ISavedObject<TSavedConstraint, ConstraintTracker, TConstraintContext>
    where TSavedConstraint : struct, ISavedConstraint<TSavedConstraint, TConstraintContext>
{
    public (int, int) DependentOn { get; }

    //public void Construct(TSaveFile originSave, AssetPoolee[] poolees, ConstraintTracker constraint);
    //public void Initialize(AssetPoolee[] poolees, Constrainer constrainer);
}
