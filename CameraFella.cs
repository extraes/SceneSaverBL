using Cysharp.Threading.Tasks;
using Jevil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SceneSaverBL;

internal static class CameraFella
{
    static GameObject cameraFella;
    static CancellationTokenSource currentOperation;

    public static void PlayScreenshot()
    {
        currentOperation?.Cancel();
        currentOperation = new();
        AsyncUtilities.WrapNoThrow(SavingStartedImpl, currentOperation.Token);
    }

    public static void MenuOpened()
    {
        currentOperation?.Cancel();
        currentOperation = new();
        AsyncUtilities.WrapNoThrow(MenuOpenedImpl, currentOperation.Token);
    }

    public static void MenuClosed()
    {
        currentOperation?.Cancel();
        currentOperation = new();
        AsyncUtilities.WrapNoThrow(MenuClosedImpl, currentOperation.Token);
    }

    public static void UpdatePosition()
    {
        if (!SelectionZone.Active || cameraFella.INOC()) return;

        (Vector3 pos, Vector3 dir) = SelectionZone.Instance.GetCameraPosAndDir();
        cameraFella.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-dir)); // negate because quads default to facing backwards i guess
    }

    static async Task SavingStartedImpl(CancellationToken token)
    {
        if (cameraFella.INOC()) await MenuOpenedImpl(token);

        cameraFella.GetComponent<Animation>().Play();
    }

    static async Task MenuOpenedImpl(CancellationToken token)
    {
        GameObject go = await Assets.Prefabs.SavingPhotographer.GetAsync();
        GameObject.Destroy(cameraFella);
        if (token.IsCancellationRequested) return;

        cameraFella = GameObject.Instantiate(go);
        cameraFella.SetActive(SelectionZone.Active);
    }

    static async Task MenuClosedImpl(CancellationToken token)
    {
        GameObject go = await Assets.Prefabs.IdlePhotographer.GetAsync();
        GameObject.Destroy(cameraFella);
        if (token.IsCancellationRequested) return;

        cameraFella = GameObject.Instantiate(go);
        cameraFella.SetActive(SelectionZone.Active && Prefs.showPreviewLocation);
    }
}
