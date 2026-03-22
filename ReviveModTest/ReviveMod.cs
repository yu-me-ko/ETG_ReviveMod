using BepInEx;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[BepInPlugin("revive.mod", "Revive Mod", "4.2")]
public class ReviveMod : BaseUnityPlugin
{
    Dictionary<PlayerController, float> glowTimers = new Dictionary<PlayerController, float>();
    Dictionary<PlayerController, int> reviveCounts = new Dictionary<PlayerController, int>();

    HashSet<PlayerController> hintedBodies = new HashSet<PlayerController>();

    private Transform[] hintPoints = new Transform[2];

    private bool isReviving = false;
    bool reviveDone = false;
    private float reviveTimer = 0f;
    private float saveTimer = 0f;
    private float saveDuration = 0.25f;
    private float reviveDuration = 3.5f;

    private PlayerController reviver;
    private PlayerController target;

    private Vector3 corpsePosP1;
    private Vector3 corpsePosP2;
    GameObject glowP1;
    GameObject glowP2;
    float glowTimer = 0f;

    // 改：补一个“是否已经记录尸体位置”的标记，避免在复活前用默认(0,0,0)误判
    private bool corpseRecordedP1 = false;
    private bool corpseRecordedP2 = false;

    bool globalHintShown = false;
    bool runInitialized = false;


    void Awake()
    {
        for (int i = 0; i < 2; i++)
        {
            GameObject obj = new GameObject("ReviveHintPoint_" + i);
            hintPoints[i] = obj.transform;
        }
    }

    void Update()
    {
        if (!GameManager.HasInstance || GameManager.Instance.PrimaryPlayer == null)
        {
            runInitialized = false;
            return;
        }

        if (!GameManager.HasInstance) return;

        TrackDeath();

        HandlePlayer(GameManager.Instance.PrimaryPlayer);
        HandlePlayer(GameManager.Instance.SecondaryPlayer);

        UpdateRevive();
        UpdateGlow();

        UpdateGlowEffect();
    }

    void UpdateGlowEffect()
    {
        glowTimer += Time.deltaTime;

        float pulse = (Mathf.Sin(glowTimer * 3f) + 1f) / 2f;

        UpdateSingleGlow(glowP1, pulse);
        UpdateSingleGlow(glowP2, pulse);
    }

    void UpdateGlow(tk2dSprite glow, float pulse)
    {
        if (glow == null) return;

        Color baseColor = new Color(0.3f, 0.7f, 1f, 0.4f);
        Color brightColor = new Color(0.7f, 0.9f, 1f, 0.8f);

        glow.color = Color.Lerp(baseColor, brightColor, pulse);
    }

    // 改：光圈改成 LineRenderer 后，这里同步更新 LineRenderer 的颜色和宽度
    void UpdateSingleGlow(GameObject glow, float pulse)
    {
        if (glow == null) return;

        var renderer = glow.GetComponent<LineRenderer>();
        if (renderer == null) return;

        // ⭐找到最近玩家
        var p1 = GameManager.Instance.PrimaryPlayer;
        var p2 = GameManager.Instance.SecondaryPlayer;

        float minDist = 999f;

        if (p1 != null)
            minDist = Mathf.Min(minDist, Vector3.Distance(p1.specRigidbody.UnitCenter, glow.transform.position));

        if (p2 != null)
            minDist = Mathf.Min(minDist, Vector3.Distance(p2.specRigidbody.UnitCenter, glow.transform.position));

        // ⭐距离影响亮度（2格内最亮）
        float distFactor = Mathf.Clamp01(1f - (minDist / 2f));

        float finalPulse = pulse * 0.5f + distFactor * 0.5f;

        Color baseColor = new Color(0.3f, 0.7f, 1f, 0.2f);
        Color brightColor = new Color(0.7f, 0.9f, 1f, 0.9f);

        Color c = Color.Lerp(baseColor, brightColor, finalPulse);

        renderer.startColor = c;
        renderer.endColor = c;

        float width = Mathf.Lerp(0.04f, 0.1f, finalPulse);
        renderer.startWidth = width;
        renderer.endWidth = width;
    }

    void UpdateGlow()
    {
        var p1 = GameManager.Instance.PrimaryPlayer;
        var p2 = GameManager.Instance.SecondaryPlayer;

        HandleGlow(p1);
        HandleGlow(p2);
    }

    void HandleGlow(PlayerController player)
    {
        if (player == null || player.sprite == null) return;

        // ⭐只对“尸体”生效
        if (!player.IsGhost)
        {
            // 恢复正常颜色
            player.sprite.color = Color.white;
            glowTimers.Remove(player);
            return;
        }

        // 初始化计时器
        if (!glowTimers.ContainsKey(player))
            glowTimers[player] = 0f;

        glowTimers[player] += Time.deltaTime;

        float t = glowTimers[player];

        // ⭐呼吸效果（核心）
        float glow = (Mathf.Sin(t * 3f) + 1f) / 2f; // 0~1

        // ⭐颜色设计（蓝色发光，你可以改）
        Color baseColor = new Color(0.3f, 0.6f, 1f);
        Color glowColor = Color.Lerp(baseColor, Color.white, glow);

        player.sprite.color = glowColor * 1.2f;
    }

    void TrackDeath()
    {
        var p1 = GameManager.Instance.PrimaryPlayer;

        if (!runInitialized && p1 != null && p1.healthHaver.IsAlive)
        {
            reviveCounts.Clear(); // ⭐清空复活次数
            runInitialized = true;

            Debug.Log("新一局，重置复活次数");
        }

        var p2 = GameManager.Instance.SecondaryPlayer;

        if (p1 != null)
        {
            if (p1.IsGhost && !corpseRecordedP1)
            {
                corpsePosP1 = p1.specRigidbody.UnitCenter;
                corpseRecordedP1 = true;
                CreateGlow(ref glowP1, corpsePosP1);
            }
            else if (!p1.IsGhost && corpseRecordedP1)
            {
                corpseRecordedP1 = false;
                if (glowP1 != null)
                {
                    Destroy(glowP1);
                    glowP1 = null;
                }
            }
        }

        if (p2 != null)
        {
            if (p2.IsGhost && !corpseRecordedP2)
            {
                corpsePosP2 = p2.specRigidbody.UnitCenter;
                corpseRecordedP2 = true;
                CreateGlow(ref glowP2, corpsePosP2);
            }
            else if (!p2.IsGhost && corpseRecordedP2)
            {
                corpseRecordedP2 = false;
                if (glowP2 != null)
                {
                    Destroy(glowP2);
                    glowP2 = null;
                }
            }
        }
    }

    // 改：把原本的方块改成“圆环光圈”，并且不加碰撞体，避免挡住救援判定
    void CreateGlow(ref GameObject glow, Vector3 pos)
    {
        if (glow != null)
        {
            Destroy(glow);
        }

        glow = new GameObject("ReviveGlow");

        glow.transform.position = pos;
        glow.transform.localScale = Vector3.one;

        var renderer = glow.AddComponent<LineRenderer>();
        renderer.useWorldSpace = false;
        renderer.loop = true;
        renderer.positionCount = 64;
        renderer.alignment = LineAlignment.View;
        renderer.numCornerVertices = 8;
        renderer.numCapVertices = 8;
        renderer.textureMode = LineTextureMode.Stretch;

        renderer.material = new Material(Shader.Find("Sprites/Default"));
        renderer.sortingOrder = 100;

        float radius = 0.55f;
        for (int i = 0; i < renderer.positionCount; i++)
        {
            float angle = (2f * Mathf.PI * i) / renderer.positionCount;
            float x = Mathf.Cos(angle) * radius;
            float y = Mathf.Sin(angle) * radius;
            renderer.SetPosition(i, new Vector3(x, y, 0f));
        }

        Color c = new Color(0.3f, 0.7f, 1f, 0.55f);
        renderer.startColor = c;
        renderer.endColor = c;
        renderer.startWidth = 0.07f;
        renderer.endWidth = 0.07f;

        // 不要碰撞体，避免影响救援
        glow.layer = 0;
    }

    void HandlePlayer(PlayerController player)
    {
        if (player == null) return;

        PlayerController other = GameManager.Instance.GetOtherPlayer(player);
        if (other == null) return;

        int idx = player.PlayerIDX;
        var input = BraveInput.GetInstanceForPlayer(idx);

        Vector3 corpsePos = (other.PlayerIDX == 0) ? corpsePosP1 : corpsePosP2;

        // 改：没有记录到尸体位置之前，不允许救援判定
        bool hasCorpse = (other.PlayerIDX == 0) ? corpseRecordedP1 : corpseRecordedP2;
        if (!hasCorpse) return;

        float dist = Vector3.Distance(player.specRigidbody.UnitCenter, corpsePos);

        bool canRevive = hasCorpse && dist < 2f;

        if (canRevive && !isReviving && player != reviver)
        {
            if (!globalHintShown)
            {
                ShowReviveHint(player, corpsePos);

                globalHintShown = true; // ⭐一局只触发一次
            }
        }

        bool interactPressed = false;

        var action = input.ActiveActions
            .GetActionFromType(GungeonActions.GungeonActionType.Interact);

        if (action != null)
            interactPressed = action.WasPressed;
        else
            interactPressed = Input.GetKeyDown(KeyCode.E);

        if (!isReviving && interactPressed && canRevive)
        {
            StartRevive(player, other);
        }

    }

    void StartRevive(PlayerController player, PlayerController other)
    {
        if (other.healthHaver != null && other.healthHaver.IsAlive)
            return;

        isReviving = true;
        reviveDone = false;
        reviveTimer = 0f;
        saveTimer = 0f;
        reviver = player;
        target = other;

        HideReviveHint(player);
        hintedBodies.Remove(other);

        Debug.Log("开始复活");

        if (GameUIRoot.Instance != null)
        {
            GameUIRoot.Instance.StartPlayerReloadBar(
                player,
                new Vector3(0f, 1.5f, 0f),
                reviveDuration
            );
        }
    }

    void UpdateRevive()
    {
        if (!isReviving || reviver == null || target == null)
            return;

        var input = BraveInput.GetInstanceForPlayer(reviver.PlayerIDX);

        float h = input.ActiveActions.Move.Value.x;
        float v = input.ActiveActions.Move.Value.y;


        if ((Mathf.Abs(h) > 0.2f || Mathf.Abs(v) > 0.2f) && saveTimer > saveDuration)
        {
            Debug.Log("移动中断救人");
            CancelRevive();
            return;
        }

        saveTimer += Time.deltaTime;
        reviveTimer += Time.deltaTime;

        //Vector3 corpsePos = (target.PlayerIDX == 0) ? corpsePosP1 : corpsePosP2;
        if (isReviving && !reviveDone && reviveTimer >= reviveDuration)
        {
            reviveDone = true;
            isReviving = false;

            StartCoroutine(ReviveFlow(target));

            target = null;
            reviver = null;
        }
    }

    IEnumerator ReviveFlow(PlayerController target)
    {
        Vector3 corpsePos = (target.PlayerIDX == 0) ? corpsePosP1 : corpsePosP2;

        Vector3 offset = (target.PlayerIDX == 0)
            ? new Vector3(-0.85f, -0.30f, 0f)
            : new Vector3(-0.80f, -0.25f, 0f);

        Vector3 finalPos = corpsePos + offset;

        Vector3 start = target.specRigidbody.transform.position;

        float t = 0f;
        float moveTime = 0.15f;

        while (t < moveTime)
        {
            t += Time.deltaTime;

            Vector3 pos = Vector3.Lerp(start, finalPos, t / moveTime);

            // ⭐正确移动方式
            target.specRigidbody.transform.position = pos;
            target.specRigidbody.Reinitialize();

            yield return null;
        }

        target.specRigidbody.transform.position = finalPos;
        target.specRigidbody.Reinitialize();

        // ⭐关键：等一帧
        yield return null;

        // ⭐用 finalPos 复活
        ReviveSystem.RevivePlayer(target, finalPos);

        yield return null;

        StartCoroutine(HandleReviveHealth(target));
    }

    private IEnumerator HandleReviveHealth(PlayerController target)
    {
        if (target == null || target.healthHaver == null)
            yield break;

        if (!reviveCounts.ContainsKey(target))
            reviveCounts[target] = 0;

        reviveCounts[target]++;
        int count = reviveCounts[target];

        float maxHealth = target.healthHaver.GetMaxHealth();
        float targetHealth = Mathf.Max(maxHealth - (count - 1), 1f);

        Debug.Log($"复活次数: {count} -> 锁定血量: {targetHealth}");

        float timer = 0f;

        // ⭐ 在0.5秒内持续压制血量
        while (timer < 1.5f)
        {
            if (target != null && target.healthHaver != null)
            {
                target.healthHaver.ForceSetCurrentHealth(targetHealth);
            }

            timer += BraveTime.DeltaTime;
            yield return null;
        }

        // ⭐ 最终再确保一次
        if (target != null && target.healthHaver != null)
        {
            target.healthHaver.ForceSetCurrentHealth(targetHealth);
        }
    }

    void CancelRevive()
    {
        if (reviver != null && GameUIRoot.Instance != null)
        {
            GameUIRoot.Instance.ForceClearReload(reviver.PlayerIDX);

            StartCoroutine(ShowFailBar(reviver));
        }

        isReviving = false;
        reviveTimer = 0f;
        saveTimer = 0f;
        reviver = null;
        target = null;
    }
    IEnumerator ShowFailBar(PlayerController player)
    {
        // ⭐先创建一个短进度条
        GameUIRoot.Instance.StartPlayerReloadBar(
            player,
            new Vector3(0f, 1.5f, 0f),
            0.3f
        );

        yield return null;

        GameUIRoot.Instance.ForceClearReload(player.PlayerIDX);
    }

    void EnsureHintPoint(int idx)
    {
        if (hintPoints[idx] == null)
        {
            GameObject obj = new GameObject("ReviveHintPoint_" + idx);
            hintPoints[idx] = obj.transform;
        }
    }

    void ShowReviveHint(PlayerController player, Vector3 pos)
    {
        int idx = player.PlayerIDX;
        EnsureHintPoint(idx);

        Transform point = hintPoints[idx];
        point.position = pos;

        string keyName = GetInteractKey(idx);
        if (string.IsNullOrEmpty(keyName)) keyName = "E";

        string text = IsChinese()
            ? "按 " + keyName + " 进行救援"
            : "Press " + keyName + " to Revive";

        if (!TextBoxManager.HasTextBox(point))
        {
            TextBoxManager.ShowTextBox(
                point.position,
                point,
                0.8f,
                text,
                string.Empty,
                false,
                TextBoxManager.BoxSlideOrientation.NO_ADJUSTMENT,
                false,
                false
            );
        }
    }

    void HideReviveHint(PlayerController player)
    {
        int idx = player.PlayerIDX;
        Transform point = hintPoints[idx];

        if (TextBoxManager.HasTextBox(point))
        {
            TextBoxManager.ClearTextBox(point);
        }
    }

    string GetInteractKey(int idx)
    {
        var input = BraveInput.GetInstanceForPlayer(idx);
        if (input == null) return "E";

        var action = input.ActiveActions
            .GetActionFromType(GungeonActions.GungeonActionType.Interact);

        if (action == null) return "E";

        foreach (var binding in action.Bindings)
        {
            var keyBinding = binding as InControl.KeyBindingSource;
            if (keyBinding != null)
            {
                return keyBinding.Control.ToString();
            }
        }

        return "E";
    }

    bool IsChinese()
    {
        return Application.systemLanguage == SystemLanguage.Chinese
            || Application.systemLanguage == SystemLanguage.ChineseSimplified
            || Application.systemLanguage == SystemLanguage.ChineseTraditional;
    }
}