namespace SceneSaverBL;

#if !UNITY_2017_1_OR_NEWER
[MelonLoader.RegisterTypeInIl2Cpp]
#endif
internal class OffsetProviderLink : MonoBehaviour
{
    // the whole point of this is to give ultevents in-editor a thing to hook onto that will give the rest of ssbl a position to use
#if !UNITY_2017_1_OR_NEWER
    public OffsetProviderLink(IntPtr ptr) : base(ptr) { }
#endif

    Transform _firePoint;
    Transform FirePoint
    {
        get
        {
            if (_firePoint == null)
            {
                _firePoint = transform.Find("FirePoint");
            }
            return _firePoint;
        }
    }

    void Start()
    {
        _ = FirePoint; // init firepoint early
    }

    public Vector3 AcquirePosition()
    {
        Vector3 outputPos;
        if (!Physics.Raycast(FirePoint.position, FirePoint.forward, out RaycastHit hit, 50f))
            outputPos = FirePoint.position + FirePoint.forward * 50;
        else
            outputPos = hit.point;

        return outputPos;
    }

    public void Clicked()
    {
        Vector3 pos = AcquirePosition();
        SceneSaverBL.Log($"Clicked at {pos}");
    }
}
