using BoneLib;
using Jevil;
using Jevil.Tweening;
using Jevil.Waiting;
using MelonLoader;
using System.Threading.Tasks;
using UnityEngine;

namespace SceneSaverBL;

[RegisterTypeInIl2Cpp]
public class ControllerTutorial : MonoBehaviour
{
    public ControllerTutorial(nint ptr) : base(ptr) { }

    const string PLAYER_PREFS_KEY = "SSBL_Shown_Tutorial";

    static bool tutorialCurrentlyActive;

    double startTime;
    Transform handL;
    Transform handR;

    // Use this for initialization
    void Start()
    {
        tutorialCurrentlyActive = !hideFlags.HasFlag(HideFlags.DontUnloadUnusedAsset);
        startTime = Time.timeAsDouble;
        CallDelayed.CallAction(Disappearize, 40);
        handL = Player.leftHand.transform;
        handR = Player.rightHand.transform;
        AutoAgroBoid aab = GetComponent<AutoAgroBoid>(); // reference smuggled a gameobject array

        for (int i = 0; i < aab.betweenAgroWaypoints.Length; i++)
            aab.betweenAgroWaypoints[i].SetActive(false);


        int start;
        int end;
        switch (Player.controllerRig.leftController.Type)
        {
            case SLZ.Marrow.Input.XRControllerType.Index:
                start = aab.bumperUpdateRate;
                end = aab.bumperUpdateRate * 2;
                break;
            default:
                start = 0;
                end = aab.bumperUpdateRate;
                break;
        }

        for (int i = start; i < end; i++)
        {
#if DEBUG
            SceneSaverBL.Log("Enabling idx " + i);
#endif
            aab.betweenAgroWaypoints[i].SetActive(true);
#if DEBUG
            SceneSaverBL.Log("Enabled " + aab.betweenAgroWaypoints[i].name);
#endif
        }
    }

    // Update is called once per frame
    void Update()
    {
        Transform head = Player.playerHead.transform;
        Vector3 handMidpoint = Vector3.Lerp(handL.position, handR.position, 0.5f);
        Vector3 dirAbnormal = head.forward + new Vector3(0, 0.25f, 0); // dirNotNormalized wasnt as short. lol.
        Vector3 dir = dirAbnormal.normalized;
        dir.y /= 2;
        Vector3 inFrontOfPlayer = head.position + 1.5f * dir;

        // so that it doesnt rotate for the first 5 seconds. this is so fucking hacky lmao
        //double rotAsDouble = Math.Max(0, Time.timeAsDouble - startTime - 5) * Const.FPI % (2 * Const.FPI);
        Vector3 posDelta = head.position - transform.position;
        /*posDelta.x =*/
        posDelta.y = 0;
        Quaternion rotation = Quaternion.LookRotation(posDelta);
        //Vector3 eulerRot = Quaternion.ToEulerAngles(rotation);
        //rotation = Quaternion.Euler(Vector3.ProjectOnPlane(eulerRot, Vector3.up));
        Vector3 desiredPos = Vector3.Lerp(inFrontOfPlayer, handMidpoint, 0.25f);
        Vector3 position = Vector3.Lerp(transform.position, desiredPos, Time.deltaTime * 5);
        transform.SetPositionAndRotation(position, rotation);
        //transform.position = position;
    }

    void OnDestroy()
    {
        tutorialCurrentlyActive = false;
    }

    void Disappearize()
    {
        transform.TweenLocalScale(Vector3.zero, 5)
            .UseCustomInterpolator((time) => Mathf.Pow(time, 4))
            .RunOnFinish(() => Destroy(gameObject));
    }

    public static void ShowIfUnseen()
    {
        if (!PlayerPrefs.HasKey(PLAYER_PREFS_KEY))
        {
            Show();

#if DEBUG
            SceneSaverBL.Log("Showing controller tutorial");
#endif
        }
    }

    public static void Show()
    {
        PlayerPrefs.TrySetInt(PLAYER_PREFS_KEY, 1);
        AsyncUtilities.WrapNoThrow(ShowImpl).RunOnFinish(SceneSaverBL.ErrIfNotNull);
    }

    static async Task ShowImpl()
    {
        GameObject tutorialPrefab = await Assets.Prefabs.ControllerTutorial.GetAsync();
        if (tutorialCurrentlyActive) return;
        GameObject instance = GameObject.Instantiate(tutorialPrefab);
        instance.AddComponent<ControllerTutorial>();
        instance.SetActive(true); // i forget if i need to set it to active so im just gonna do that
    }
}
