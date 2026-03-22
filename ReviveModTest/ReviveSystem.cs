using System.Reflection;
using UnityEngine;
using System.Collections;

public class ReviveSystem
{
    public static void RevivePlayer(PlayerController target, Vector3 pos)
    {
        var method = typeof(PlayerController).GetMethod(
            "CoopResurrectInternal",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        if (!object.ReferenceEquals(method, null) &&
            !object.ReferenceEquals(target, null) &&
            target.IsGhost)
        {
            IEnumerator coroutine = (IEnumerator)method.Invoke(
                target,
                new object[] { pos, null, false }
            );

            target.StartCoroutine(coroutine);
        }
    }
}