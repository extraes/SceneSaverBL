using BoneLib;
using Cysharp.Threading.Tasks.Internal;
using Jevil;
using LuxURPEssentials;
using SLZ;
using SLZ.Interaction;
using SLZ.Marrow.Input;
using SLZ.Marrow.Pool;
using SLZ.Marrow.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnhollowerBaseLib.Attributes;
using UnityEngine;

namespace SceneSaverBL;

[MelonLoader.RegisterTypeInIl2Cpp]
internal class SelectionZone : MonoBehaviour
{
    private ParticleSystem selectionWireSys;
    private Transform selectionWireT;
    private BoxCollider col;

    public float CornerDistance => Vector3.Distance(leftPos, rightPos);
    public Bounds Bounds
    {
        get
        {
            Bounds bounds = new(transform.position, Vector3.zero);
            bounds.Encapsulate(leftPos);
            bounds.Encapsulate(rightPos);
            return bounds;
        }
    }

    Vector3 leftPos;
    Vector3 rightPos;
    internal Dictionary<int, AssetPoolee> pooleesInSelectionZone = new();

    private static bool instanceExists;
    private static SelectionZone instance;
    internal static SelectionZone Instance
    {
        get
        {
            if (instanceExists)
                return instance;
            else
            {
#if DEBUG
                if (instance is not null) SceneSaverBL.Warn($"SelectionZone object '{nameof(SelectionZone)}.{nameof(instance)}' was collected/destroyed in the Unity domain without notifying the managed domain.");
#endif
                return null;
            }
        }
    }

    public static bool Active => !instance.INOC() && instance.gameObject.active;

    public SelectionZone(IntPtr ptr) : base(ptr) { }

    void Awake()
    {
        instance = this;
        instanceExists = true;
        GameObject selWire = GameObject.Instantiate(Assets.Prefabs.SelectionWire.Get());
        selWire.transform.parent = transform;
        selectionWireSys = selWire.GetComponent<ParticleSystem>();
        selectionWireT = selWire.transform;

        //GameObject grabbable1 = GameObject.Instantiate(SceneSaverBL.grabbablePrefab);
        //GameObject grabbable2 = GameObject.Instantiate(SceneSaverBL.grabbablePrefab);
        //grabbableChildren = new Transform[] { grabbable1.transform.GetChild(0), grabbable2.transform.GetChild(0) };

        col = gameObject.AddComponent<BoxCollider>();
        col.isTrigger = true;

        ControllerTutorial.ShowIfUnseen();
    }

    void OnEnable()
    {
        selectionWireT.localPosition = Vector3.zero;
        selectionWireT.localRotation = Quaternion.identity;

        leftPos = transform.position + (Vector3.one / 4);
        rightPos = transform.position - (Vector3.one / 4);
        SceneSaverBL.Log($"Selection wire enabled: lpos = {leftPos} ; rpos = {rightPos}");
        Resize();

        SelectionParticles.WireEnabled();

#if DEBUG
        if (UnityEngine.Random.Range(0, 2) == 1)
#else
        if (UnityEngine.Random.Range(0, 250) == 1)
#endif
            PostEnable();
    }

    void OnDisable()
    {
        pooleesInSelectionZone.Clear();
        SelectionParticles.WireDisabled();
        if (transform.childCount != 1) transform.GetChild(1).gameObject.Destroy();
    }

    void Update()
    {
        SelectionParticles.Thunk();

        float oldSum = (leftPos + rightPos).magnitude;
        DoPosCheck(Player.leftHand, ref leftPos);
        DoPosCheck(Player.rightHand, ref rightPos);
        float newSum = (leftPos + rightPos).magnitude;
        if (oldSum == newSum) return;

        Resize();
        CleanList();
    }

    [HideFromIl2Cpp]
    void Resize()
    {
        Bounds bounds = Bounds;

        transform.position = bounds.center;
        transform.localScale = bounds.size;

        float calcedRate = Mathf.Pow(transform.localScale.magnitude / 4, 2) * 128;
        float minRate = 32;
        float maxRate = selectionWireSys.maxParticles / selectionWireSys.startLifetime;
        selectionWireSys.emissionRate = Mathf.Clamp(calcedRate, minRate, maxRate);
    }

    [HideFromIl2Cpp]
    private void DoPosCheck(Hand hand, ref Vector3 pos)
    {
        bool stickClicked = hand.Controller.GetThumbStick() || Prefs.dontUseStickClick;
        if (!stickClicked) return;
        HandPoseAnimator hpa = hand.Animator;
        if (hpa._currentThumb + hpa._currentIndex + hpa._currentMiddle + hpa._currentRing + hpa._currentPinky < 4.7f) return;

        pos = hpa.transform.position;
    }

    // send trigger events to a queue to avoid lag
    void OnTriggerEnter(Collider other)
    {
        //SceneSaverBL.Log($"REMOVEME: ");
        if (1 << other.gameObject.layer == (int)GameLayers.STATIC)
        {
            Physics.IgnoreCollision(col, other);
            return;
        }

        SelectionParticles.TriggerEvent(other, false);
    }

    void OnTriggerExit(Collider other)
    {
        SelectionParticles.TriggerEvent(other, true);
    }

    void OnDestroy()
    {
        instanceExists = false;
    }

    // returns true if any item was removed
    [HideFromIl2Cpp]
    bool CleanList()
    {
        try
        {
            int iid = -1;
            foreach (var kvp in pooleesInSelectionZone)
            {
                if (DoesObjectWithInstanceIDExist(kvp.Key)) continue;
                iid = kvp.Key;
            }

            if (iid == -1) return false;
            pooleesInSelectionZone.Remove(iid);
            CleanList();
            return true;
        }
#if DEBUG
        catch(Exception ex) 
        {
            SceneSaverBL.Error(ex);
            return false;
        }
#else
        catch
        { 
            return false; 
        }
#endif
    }

    [HideFromIl2Cpp]
    public AssetPoolee[] GetPoolees()
    {
        // order by y position (ascending) so that things like tables are loaded first when deserializing/initializing from save file
        return pooleesInSelectionZone.Values.Distinct(UnityObjectComparer<AssetPoolee>.Instance).OrderBy(poolee => poolee.transform.position.y).ToArray();
    }

    [HideFromIl2Cpp]
    public Camera CreateCamera()
    {
        // i *could* put the VolumetricRendering component on the camera, but thats added setup & rendering time and impedes visual clarity
        Vector3 max = Vector3.Max(leftPos, rightPos);
        Vector3 min = Vector3.Min(leftPos, rightPos);

        (Vector3 pos, Vector3 dir) = GetCameraPosAndDir(max, min);
        float nearClip = 0.05f;
        if (dir == default)
        {
            pos = max;
            dir = min - max;
            nearClip = Vector3.Distance(max, min) / 4;
        }

        // particles and wire around selected objects are on water, and just want to ignore ui cuz someone might want their avatar to appear but BoneMenu would obscure the selection
        int layers = ~(int)GameLayers.WATER;
        layers |= (int)GameLayers.SPAWNGUN_UI;
        layers |= (int)GameLayers.UI;
        GameObject cameraGo = new("SceneSaver Temporary Camera");
        Camera cam = cameraGo.AddComponent<Camera>();
        cameraGo.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir));
        cam.nearClipPlane = nearClip;
        cam.aspect = 1;
        cam.cullingMask = layers;
        cam.enabled = false;

        return cam;
    }

    [HideFromIl2Cpp]
    internal (Vector3, Vector3) GetCameraPosAndDir()
    {
        Vector3 max = Vector3.Max(leftPos, rightPos);
        Vector3 min = Vector3.Min(leftPos, rightPos);

        (Vector3 pos, Vector3 dir) = GetCameraPosAndDir(max, min);
        if (dir == default)
        {
            pos = max;
            dir = min - max;
        }

        return new (pos, dir);
    }

    [HideFromIl2Cpp]
    (Vector3, Vector3) GetCameraPosAndDir(Vector3 max, Vector3 min)
    {
        // i could probably optimize this some, but i dont think it really matters too much, on account of all of this being stackalloc

        Vector3 center = (max + min) / 2;
        float distToCenter = Vector3.Distance(max, center);
        // only care if static geo occludes camera.... but SLZ doesnt always mark colliders as static... thanks.
        int layerMask = (int)GameLayers.STATIC;
        layerMask |= (int)GameLayers.DEFAULT; // include Default because SLZ loooooves putting static geo under default. what else will this break? fuck if i know.

        // use V3.Lerp to slightly inset from edge, in case edge is in something... but not *too* in something?

        // top = max.y
        // capital letters are what elements are taken from max
        Vector3 topXZ = max;
        Vector3 topX = Vector3.Lerp(new Vector3(max.x, max.y, min.z), center, 0.1f);
        Vector3 topZ = Vector3.Lerp(new Vector3(min.x, max.y, max.z), center, 0.1f);
        Vector3 topNeg = Vector3.Lerp(new Vector3(min.x, max.y, min.z), center, 0.1f);

        // bottom = min.y
        // antithesis of the top variants
        Vector3 bottomXZ = min;
        Vector3 bottomX = Vector3.Lerp(new Vector3(min.x, min.y, max.z), center, 0.1f);
        Vector3 bottomZ = Vector3.Lerp(new Vector3(max.x, min.y, min.z), center, 0.1f);
        Vector3 bottomNeg = Vector3.Lerp(new Vector3(max.x, min.y, max.z), center, 0.1f);

        // create pairs so its easier to work with
        (Vector3 top, Vector3 dir) corner1 = (topXZ, bottomXZ - topXZ);
        (Vector3 top, Vector3 dir) corner2 = (topX, bottomX - topX);
        (Vector3 top, Vector3 dir) corner3 = (topZ, bottomZ - topZ);
        (Vector3 top, Vector3 dir) corner4 = (topNeg, bottomNeg - topNeg);

        // ideally i'd use an array and foreach it, but thats a heap alloc i can avoid just fine

        // this goes slightly past the center, but oh well its fine
        if (!Physics.Raycast(corner1.top, corner1.dir, distToCenter, layerMask))
            return corner1;
        if (!Physics.Raycast(corner2.top, corner2.dir, distToCenter, layerMask))
            return corner2;
        if (!Physics.Raycast(corner3.top, corner3.dir, distToCenter, layerMask))
            return corner3;
        if (!Physics.Raycast(corner4.top, corner4.dir, distToCenter, layerMask))
            return corner4;

#if DEBUG
        // avoid COMPLETE logspam
        if (Time.frameCount % 10 == 0) SceneSaverBL.Log("No corners have a clear line of sight to the center!");
#endif
        return default;
    }

    void PostEnable()
    {
        AsyncUtilities.WrapNoThrow(PostEnableImpl).RunOnFinish(SceneSaverBL.ErrIfNotNull);
    }

    async Task PostEnableImpl()
    {
        if (this.INOC()) return;
        GameObject prefab = await Assets.Prefabs.SelectionWireMusic.GetAsync();
        if (this.INOC()) return;
        GameObject musPlayer = GameObject.Instantiate(prefab);
        musPlayer.transform.SetParent(transform, false);
        musPlayer.transform.localPosition = Vector3.zero;
    }
}
