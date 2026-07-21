using System;
using System.Collections.Generic;
using System.Linq;
using Hazel;
using SuperNewRoles.Events;
using SuperNewRoles.Events.PCEvents;
using SuperNewRoles.Modules;
using SuperNewRoles.Modules.Events.Bases;
using SuperNewRoles.Patches;
using SuperNewRoles.Roles.Ability.CustomButton;
using SuperNewRoles.Roles.Impostor;
using SuperNewRoles.CustomCosmetics.CosmeticsPlayer;
using UnityEngine;

namespace SuperNewRoles.Roles.Ability;

public record MatryoshkaData(bool WearReport, int WearLimit, float WearTime, float AdditionalKillCoolTime, float CoolTime);
public class MatryoshkaAbility : CustomButtonBase, IButtonEffect, IAbilityCount
{
    public DeadBody currentWearingBody { get; private set; }
    private PlayerControl targetPlayer;

    public override Sprite Sprite => AssetManager.GetAsset<Sprite>(currentWearingBody != null ? "MatryoshkaTakeOffButton.png" : "MatryoshkaPutOnButton.png");
    public override string buttonText => currentWearingBody != null ? ModTranslation.GetString("MatryoshkaTakeOffButtonName") : ModTranslation.GetString("MatryoshkaPutOnButtonName");
    protected override KeyType keytype => KeyType.Ability1;
    public override float DefaultTimer => Data.CoolTime;

    public MatryoshkaData Data { get; }

    public bool isEffectActive { get; set; }

    public Action OnEffectEnds => () => RpcSetMatryoshkaDeadBody(this, null, false, Player.transform.position);
    public float EffectDuration => Data.WearTime;
    public virtual bool effectCancellable => true;

    public float EffectTimer { get; set; }

    private int Counter = 0;
    // 着用前の自分のカラーIDを保存（setOutfitがDefaultOutfitを上書きするため着用時に事前保存が必要）
    private int _originalColorId = -1;
    // SetHat/SetVisor は Hats[Default]/Visors[Default] を直接上書きするため、
    // 着用中にこの辞書エントリが死体側のコスメで上書きされてしまう。
    // FinishShapeshift() は Hats[Default] を再参照するだけで新しいデータを設定しないため、
    // このまま脱ぐと本人のコスメには戻らず、死体のコスメが残ったままになる。
    // 着用前に本人の実データを退避しておき、脱ぐ際に直接書き戻す。
    private ICosmeticData _originalHat1Data;
    private ICosmeticData _originalHat2Data;
    private ICosmeticData _originalVisor1Data;
    private ICosmeticData _originalVisor2Data;

    private CustomKillButtonAbility customKillButtonAbility;

    private EventListener _fixedUpdateListener;
    private EventListener<DieEventData> _dieEventListener;
    private EventListener<CalledMeetingEventData> _calledMeetingEventListener;

    public override ShowTextType showTextType => ShowTextType.ShowWithCount;

    public MatryoshkaAbility(MatryoshkaData data) : base()
    {
        Data = data;
        Count = Data.WearLimit;
    }

    public override bool CheckIsAvailable()
    {
        // ファングルのキノコサボタージュ中は使用不可。
        // サボ終了時に Among Us が全員の見た目をリセットするため、
        // 着用中の外見が上書きされる危険がある。
        if (SaboStateTracker.activeSaboTypes.Contains(SystemTypes.MushroomMixupSabotage))
            return false;

        targetPlayer = GetClosestDeadBody();
        return targetPlayer != null && PlayerControl.LocalPlayer.CanMove;
    }
    public override bool CheckHasButton()
    {
        return base.CheckHasButton() && (HasCount || isEffectActive);
    }

    public override void AttachToAlls()
    {
        base.AttachToAlls();
        customKillButtonAbility = new CustomKillButtonAbility(
            canKill: () => true,
            killCooldown: () => GameOptionsManager.Instance.CurrentGameOptions.GetFloat(AmongUs.GameOptions.FloatOptionNames.KillCooldown) + Data.AdditionalKillCoolTime * Counter,
            onlyCrewmates: () => true
        );

        Player.AttachAbility(customKillButtonAbility, new AbilityParentAbility(this));

        _fixedUpdateListener = FixedUpdateEvent.Instance.AddListener(OnFixedUpdate);
        _dieEventListener = DieEvent.Instance.AddListener(OnDie);
        _calledMeetingEventListener = CalledMeetingEvent.Instance.AddListener(OnCalledMeeting);
    }

    public override void DetachToAlls()
    {
        UnlockMatryoshka(this, Player.transform.position);
        base.DetachToAlls();
        _fixedUpdateListener?.RemoveListener();
        _dieEventListener?.RemoveListener();
        _calledMeetingEventListener?.RemoveListener();
    }

    private void OnFixedUpdate()
    {
        if (currentWearingBody != null)
            // 死体を自分の位置に移動
            currentWearingBody.transform.position = Player.transform.position;
    }

    private void OnDie(DieEventData data)
    {
        if (!Player.AmOwner || data.player?.PlayerId != Player.PlayerId) return;
        if (currentWearingBody != null)
            RpcSetMatryoshkaDeadBody(this, null, false, Player.transform.position);
    }

    private void OnCalledMeeting(CalledMeetingEventData data)
    {
        if (Player.AmOwner && currentWearingBody != null)
            RpcSetMatryoshkaDeadBody(this, null, false, Vector3.zero);
    }
    public override void OnClick()
    {
        if (targetPlayer == null) return;

        PlayerControl localPlayer = PlayerControl.LocalPlayer;
        bool isWearing = currentWearingBody != null;

        if (isWearing)
        {
            // 着用している死体を脱ぐ
            RpcSetMatryoshkaDeadBody(this, null, false, localPlayer.transform.position);
        }
        else
        {
            // 新しい死体を着る
            DeadBody targetBody = GetBodyByPlayerId(targetPlayer.PlayerId);
            if (targetBody != null)
                RpcSetMatryoshkaDeadBody(this, targetPlayer, true, Vector3.zero);
            Counter++;
            this.UseAbilityCount();
        }
    }

    private PlayerControl GetClosestDeadBody()
    {
        var localPlayer = PlayerControl.LocalPlayer;
        float closestDistance = float.MaxValue;
        PlayerControl result = null;

        // 既に着用中の場合は、その死体を返す
        if (currentWearingBody != null)
        {
            return ExPlayerControl.ById(currentWearingBody.ParentId);
        }

        // 死体を探す
        DeadBody[] deadBodies = UnityEngine.Object.FindObjectsOfType<DeadBody>();
        foreach (DeadBody body in deadBodies)
        {
            // 既に誰かが着用している死体はスキップ
            if (ExPlayerControl.ExPlayerControls.Any(x =>
                (x.Role == RoleId.Matryoshka && x.GetAbility<MatryoshkaAbility>()?.currentWearingBody == body) ||
                (x.Role == RoleId.Owl && x.GetAbility<OwlDeadBodyTransportAbility>()?.DeadBodyInTransport == body)
            )) continue;

            float distance = Vector2.Distance(localPlayer.transform.position, body.transform.position);
            if (distance <= 2f && distance < closestDistance)
            {
                closestDistance = distance;
                result = ExPlayerControl.ById(body.ParentId);
            }
        }

        return result;
    }

    private DeadBody GetBodyByPlayerId(byte playerId)
    {
        DeadBody[] deadBodies = UnityEngine.Object.FindObjectsOfType<DeadBody>();
        foreach (DeadBody body in deadBodies)
        {
            if (body.ParentId == playerId)
            {
                return body;
            }
        }
        return null;
    }

    private static void UnlockMatryoshka(MatryoshkaAbility source, Vector3 position)
    {
        DeadBody deadBody = source.currentWearingBody;
        if (deadBody != null)
        {
            // 着用前の色ID（setOutfitがDefaultOutfitを上書きしてしまうため事前保存した値）で復元
            int restoreColorId = source._originalColorId >= 0
                ? source._originalColorId
                : source.Player.Data.DefaultOutfit.ColorId;

            // バニラ見た目を元に戻す
            source.Player.Player.setOutfit(source.Player.Data.DefaultOutfit);

            // カスタムコスメティクス4層を Default に戻す。
            // 着用中に Hats[Default]/Visors[Default] が死体のコスメで上書きされているため、
            // FinishShapeshift の前に退避しておいた本人の実データを直接書き戻す。
            var layer = CustomCosmeticsLayers.ExistsOrInitialize(source.Player.Player.cosmetics);
            if (layer.hat1 != null)
            {
                layer.hat1.gameObject.SetActive(true);
                if (source._originalHat1Data != null) layer.hat1.Hats[CustomOutfitType.Default] = source._originalHat1Data;
                layer.hat1.FinishShapeshift(restoreColorId);
            }
            if (layer.hat2 != null)
            {
                layer.hat2.gameObject.SetActive(true);
                if (source._originalHat2Data != null) layer.hat2.Hats[CustomOutfitType.Default] = source._originalHat2Data;
                layer.hat2.FinishShapeshift(restoreColorId);
            }
            if (layer.visor1 != null)
            {
                layer.visor1.gameObject.SetActive(true);
                if (source._originalVisor1Data != null) layer.visor1.Visors[CustomOutfitType.Default] = source._originalVisor1Data;
                layer.visor1.FinishShapeshift(restoreColorId);
            }
            if (layer.visor2 != null)
            {
                layer.visor2.gameObject.SetActive(true);
                if (source._originalVisor2Data != null) layer.visor2.Visors[CustomOutfitType.Default] = source._originalVisor2Data;
                layer.visor2.FinishShapeshift(restoreColorId);
            }

            source._originalColorId = -1;
            source._originalHat1Data = null;
            source._originalHat2Data = null;
            source._originalVisor1Data = null;
            source._originalVisor2Data = null;

            // 死体を報告可能に戻す
            deadBody.Reported = false;
            foreach (SpriteRenderer renderer in deadBody.bodyRenderers)
                renderer.enabled = true;
            deadBody.myCollider.enabled = true;
            deadBody.transform.position = position;
        }
        source.currentWearingBody = null;
    }

    [CustomRPC]
    public static void RpcSetMatryoshkaDeadBody(MatryoshkaAbility source, ExPlayerControl target, bool isWearing, Vector3 position)
    {
        if (source == null) return;

        if (!isWearing)
        {
            UnlockMatryoshka(source, position);
        }
        else
        {
            // 新しい死体を着用
            if (target == null) return;

            DeadBody targetBody = null;
            DeadBody[] deadBodies = UnityEngine.Object.FindObjectsOfType<DeadBody>();
            foreach (DeadBody body in deadBodies)
            {
                if (body.ParentId == target.PlayerId)
                {
                    targetBody = body;
                    break;
                }
            }

            if (targetBody == null) return;

            // setOutfit は DefaultOutfit を上書きするため、先に自分の色を保存する
            source._originalColorId = source.Player.Data.DefaultOutfit.ColorId;

            // SetHat/SetVisor が Hats[Default]/Visors[Default] を破壊する前に、
            // 本人の実データ（ICosmeticDataオブジェクト）を退避しておく
            var sourceLayerForBackup = CustomCosmeticsLayers.ExistsOrInitialize(source.Player.Player.cosmetics);
            source._originalHat1Data = sourceLayerForBackup.hat1?.DefaultHat;
            source._originalHat2Data = sourceLayerForBackup.hat2?.DefaultHat;
            source._originalVisor1Data = sourceLayerForBackup.visor1?.DefaultVisor;
            source._originalVisor2Data = sourceLayerForBackup.visor2?.DefaultVisor;

            // バニラ見た目を死体の人に合わせる
            source.Player.Player.setOutfit(target.Data.DefaultOutfit);

            // 自分（source）の CustomCosmeticsLayer に、死体（target）の hat/visor をコピーする。
            // これにより「死体の人と同じカスタムコスメが source に表示」され、
            // target 側の layer には一切触れないため、死体のカスタムコスメが消えることもない。
            var sourceLayer = CustomCosmeticsLayers.ExistsOrInitialize(source.Player.Player.cosmetics);
            var targetLayer = CustomCosmeticsLayers.ExistsOrInitialize(target.Player.cosmetics);
            int targetColorId = target.Data.DefaultOutfit.ColorId;

            if (sourceLayer.hat1 != null)
                sourceLayer.hat1.SetHat(targetLayer.hat1?.DefaultHat?.ProdId ?? HatData.EmptyId, targetColorId);
            if (sourceLayer.hat2 != null)
                sourceLayer.hat2.SetHat(targetLayer.hat2?.DefaultHat?.ProdId ?? HatData.EmptyId, targetColorId);
            if (sourceLayer.visor1 != null)
                sourceLayer.visor1.SetVisor(targetLayer.visor1?.DefaultVisor?.ProdId ?? VisorData.EmptyId, targetColorId);
            if (sourceLayer.visor2 != null)
                sourceLayer.visor2.SetVisor(targetLayer.visor2?.DefaultVisor?.ProdId ?? VisorData.EmptyId, targetColorId);

            // 報告不可能にする
            targetBody.Reported = !source.Data.WearReport;

            // レンダラーを非表示にする
            foreach (SpriteRenderer renderer in targetBody.bodyRenderers)
            {
                renderer.enabled = false;
            }

            targetBody.myCollider.enabled = source.Data.WearReport;

            source.currentWearingBody = targetBody;
        }
    }

    public override void OnMeetingEnds()
    {
        base.OnMeetingEnds();

        // 会議終了時に自動的に死体を脱ぐ
        if (currentWearingBody != null)
        {
            RpcSetMatryoshkaDeadBody(this, null, false, Vector3.zero);
        }
    }
}
