using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SceneSaverBL.Versions.Version5;

internal struct TransformInitializationContext5
{
    public Transform transform;

    public TransformInitializationContext5(Transform t)
    {
        this.transform = t;
    }
}
