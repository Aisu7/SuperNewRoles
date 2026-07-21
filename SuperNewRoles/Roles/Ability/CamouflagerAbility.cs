using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using SuperNewRoles.Modules;
using SuperNewRoles.Roles.Impostor;
using SuperNewRoles.Modules.Events.Bases;
using SuperNewRoles.Events;
using SuperNewRoles.Roles.Ability.CustomButton;
using SuperNewRoles.CustomCosmetics.CosmeticsPlayer;
using SuperNewRoles.Extensions;

namespace SuperNewRoles.Roles.Ability;

public class CamouflagerAbility : AbilityBase
{
    public float CoolTime;
    public float DurationTime;
    public int CamouflageColor;
    public int ChangeColorType;

    private CamouflageButtonAbility _camouflageButtonAbility;
    private Dictionary<byte, PlayerOutfitData> _originalOutfits = new();
    public bool _isCamouflaged { get; private set; }

    private EventListener<MeetingStartEventData> _meetingStartListener;
    private EventListener<WrapUpEventData> _wrapUpListener;

    public CamouflagerAbility(CamouflagerAbilityOption option)
    {
        CoolTime = option.CoolTime;
        DurationTime = option.DurationTime;
        CamouflageColor = option.CamouflageColor;
        ChangeColorType = option.ChangeColorType;
    }

    public override void AttachToAlls()
    {
        base.AttachToAlls();

        _camouflageButtonAbility = new CamouflageButtonAbility(CoolTime, DurationTime, this);
        _meetingStartListener = MeetingStartEvent.Instance.AddListener(OnMeetingStart);
        _wrapUpListener = WrapUpEvent.Instance.AddListener(OnWrapUp);
        Player.AttachAbility(_camouflageButtonAbility, new AbilityParentAbility(this));
    }

    public void OnMeetingStart(MeetingStartEventData data)
    {
        EndCamouflage();
    }

    public void OnWrapUp(WrapUpEventData data)
    {
        // 会議中にカモフラが終了した場合、会議明けに元の外見に戻す
        // MeetingStartEvent で EndCamouflage() を呼ぶが、
        // 会議明けの WrapUp でも元の外見が正しく反映されるよう再適用する
        if (_isCamouflaged) return; // まだカモフラ中なら何もしない
        if (_originalOutfits.Count == 0) return; // 既に復元済みなら何もしない

        // 会議前にカモフラ終了した = _originalOutfits に元データが残っている場合のみ復元
        foreach (var player in PlayerControl.AllPlayerControls)
        {
            if (player == null || player.Data.Disconnected) continue;
            if (!_originalOutfits.ContainsKey(player.PlayerId)) continue;

            var originalOutfit = _originalOutfits[player.PlayerId];
            var outfit = new NetworkedPlayerInfo.PlayerOutfit
            {
                PlayerName = originalOutfit.PlayerName,
                ColorId = originalOutfit.ColorId,
                SkinId = originalOutfit.SkinId,
                HatId = originalOutfit.Hat2Id, // バニラのハットは hat2 に対応するため Hat2Id を使用
                VisorId = originalOutfit.Visor2Id, // バニラのバイザーは visor2 に対応するため Visor2Id を使用
                PetId = originalOutfit.PetId
            };
            player.setOutfit(outfit);

            CustomCosmeticsLayer layer = CustomCosmeticsLayers.ExistsOrInitialize(player.cosmetics);
            layer.hat1.gameObject.SetActive(true);
            if (originalOutfit.Hat1Data != null)
                layer.hat1.Hats[CustomOutfitType.Default] = originalOutfit.Hat1Data;
            layer.hat1.FinishShapeshift(originalOutfit.ColorId);

            layer.visor1.gameObject.SetActive(true);
            if (originalOutfit.Visor1Data != null)
                layer.visor1.Visors[CustomOutfitType.Default] = originalOutfit.Visor1Data;
            layer.visor1.FinishShapeshift(originalOutfit.ColorId);

            layer.hat2.gameObject.SetActive(true);
            layer.hat2.FinishShapeshift(originalOutfit.ColorId);
            layer.visor2.gameObject.SetActive(true);
            layer.visor2.FinishShapeshift(originalOutfit.ColorId);
        }
        _originalOutfits.Clear();
    }

    public override void DetachToAlls()
    {
        EndCamouflage();
        base.DetachToAlls();
        _meetingStartListener?.RemoveListener();
        _wrapUpListener?.RemoveListener();
    }

    [CustomRPC]
    public void RpcStartCamouflage()
    {
        StartCamouflage();
    }

    [CustomRPC]
    public void RpcEndCamouflage()
    {
        EndCamouflage();
    }

    private void StartCamouflage()
    {
        if (_isCamouflaged) return;

        _isCamouflaged = true;
        _originalOutfits.Clear();

        // 全プレイヤーの元の外見を保存
        foreach (var player in PlayerControl.AllPlayerControls)
        {
            if (player == null || player.Data.Disconnected) continue;

            CustomCosmeticsLayer layer = CustomCosmeticsLayers.ExistsOrInitialize(player.cosmetics);

            _originalOutfits[player.PlayerId] = new PlayerOutfitData
            {
                PlayerName = player.Data.PlayerName,
                ColorId = player.Data.DefaultOutfit.ColorId,
                SkinId = player.Data.DefaultOutfit.SkinId,
                Hat1Id = layer.hat1?.DefaultHat?.ProdId ?? "ERROR",
                Hat2Id = layer.hat2?.DefaultHat?.ProdId ?? "ERROR",
                Visor1Id = layer.visor1?.DefaultVisor?.ProdId ?? "ERROR",
                Visor2Id = layer.visor2?.DefaultVisor?.ProdId ?? "ERROR",
                PetId = player.Data.DefaultOutfit.PetId,
                // setOutfit による Hats[Default] 破壊から守るため、実データも直接退避する
                Hat1Data = layer.hat1?.DefaultHat,
                Visor1Data = layer.visor1?.DefaultVisor
            };
        }

        // カモフラージュを適用
        ApplyCamouflage();
    }

    private void ApplyCamouflage()
    {
        var camouflageOutfit = new NetworkedPlayerInfo.PlayerOutfit
        {
            PlayerName = "　",
            ColorId = GetCamouflageColor(),
            SkinId = "",
            HatId = "",
            VisorId = "",
            PetId = ""
        };

        foreach (var player in PlayerControl.AllPlayerControls)
        {
            if (player == null || player.Data.Disconnected) continue;

            // ランダムカラーの場合は個別に色を設定
            if (ChangeColorType == 2) // Random
            {
                camouflageOutfit.ColorId = GetRandomColorForPlayer(player.PlayerId);
            }

            player.setOutfit(camouflageOutfit);

            CustomCosmeticsLayer layer = CustomCosmeticsLayers.ExistsOrInitialize(player.cosmetics);
            // hat1/visor1 は StartCamouflage で EmptyHat に差し替え（正常に隠れる）
            layer.hat1.StartCamouflage(camouflageOutfit.ColorId);
            layer.visor1.StartCamouflage(camouflageOutfit.ColorId);
            // hat2/visor2 は LateUpdate() が毎フレーム sprite と FlipX を上書きするため
            // StartCamouflage を呼んでも虹色・向き固定になってしまう。
            // SetActive(false) で物理的に非表示にすることで LateUpdate の影響を回避する。
            layer.hat2.gameObject.SetActive(false);
            layer.visor2.gameObject.SetActive(false);
        }
    }

    private int GetCamouflageColor()
    {
        return ChangeColorType switch
        {
            0 => 15, // Fixed - Gray
            1 => CamouflageColor, // Select
            2 => 15, // Random - Default Gray (個別に設定される)
            _ => 15
        };
    }

    private int GetRandomColorForPlayer(byte playerId)
    {
        if (!_originalOutfits.ContainsKey(playerId)) return 15;

        var allColors = _originalOutfits.Values.Select(o => o.ColorId).ToList();
        var playerOriginalColor = _originalOutfits[playerId].ColorId;

        // 自分の色以外からランダム選択
        var availableColors = allColors.Where(c => c != playerOriginalColor).ToList();
        if (availableColors.Count == 0) return 15;

        return ModHelpers.GetRandom(availableColors);
    }

    private void EndCamouflage()
    {
        if (!_isCamouflaged) return;

        _isCamouflaged = false;

        // 他のカモフラがいればそのまま
        if (ExPlayerControl.ExPlayerControls.Any(x => x.TryGetAbility<CamouflagerAbility>(out var ability) && ability._isCamouflaged)) return;

        // 元の外見に戻す
        foreach (var player in PlayerControl.AllPlayerControls)
        {
            if (player == null || player.Data.Disconnected) continue;
            if (!_originalOutfits.ContainsKey(player.PlayerId)) continue;

            var originalOutfit = _originalOutfits[player.PlayerId];
            var outfit = new NetworkedPlayerInfo.PlayerOutfit
            {
                PlayerName = originalOutfit.PlayerName,
                ColorId = originalOutfit.ColorId,
                SkinId = originalOutfit.SkinId,
                HatId = originalOutfit.Hat2Id, // バニラのハットは hat2 に対応するため Hat2Id を使用
                VisorId = originalOutfit.Visor2Id, // バニラのバイザーは visor2 に対応するため Visor2Id を使用
                PetId = originalOutfit.PetId
            };

            player.setOutfit(outfit);

            CustomCosmeticsLayer layer = CustomCosmeticsLayers.ExistsOrInitialize(player.cosmetics);

            // hat1/visor1: setOutfit(HatId="")が Hats[Default]/Visors[Default] を破壊しているため、
            // FinishShapeshift だけでは復元できない。退避しておいた実データを直接辞書へ書き戻してから
            // FinishShapeshift で描画を反映させる。
            layer.hat1.gameObject.SetActive(true);
            if (originalOutfit.Hat1Data != null)
                layer.hat1.Hats[CustomOutfitType.Default] = originalOutfit.Hat1Data;
            layer.hat1.FinishShapeshift(originalOutfit.ColorId);

            layer.visor1.gameObject.SetActive(true);
            if (originalOutfit.Visor1Data != null)
                layer.visor1.Visors[CustomOutfitType.Default] = originalOutfit.Visor1Data;
            layer.visor1.FinishShapeshift(originalOutfit.ColorId);

            // hat2/visor2は SetActive(false) のみで内部データは壊れていないため、
            // 通常通り FinishShapeshift で復元できる
            layer.hat2.gameObject.SetActive(true);
            layer.hat2.FinishShapeshift(originalOutfit.ColorId);
            layer.visor2.gameObject.SetActive(true);
            layer.visor2.FinishShapeshift(originalOutfit.ColorId);
        }

        _originalOutfits.Clear();

        NameText.UpdateAllNameInfo();
    }

    public class CamouflagerAbilityOption
    {
        public float CoolTime;
        public float DurationTime;
        public int CamouflageColor;
        public int ChangeColorType;

        public CamouflagerAbilityOption(float coolTime, float durationTime, int camouflageColor, int changeColorType)
        {
            CoolTime = coolTime;
            DurationTime = durationTime;
            CamouflageColor = camouflageColor;
            ChangeColorType = changeColorType;
        }
    }

    private class PlayerOutfitData
    {
        public string PlayerName { get; set; }
        public int ColorId { get; set; }
        public string SkinId { get; set; }
        public string Hat1Id { get; set; }
        public string Hat2Id { get; set; }
        public string Visor1Id { get; set; }
        public string Visor2Id { get; set; }
        public string PetId { get; set; }
        // setOutfit(HatId="") を呼ぶと、CosmeticsPatches 経由で hat1.SetHat("", color) が
        // 実行され、hat1.Hats[CustomOutfitType.Default] が空/null なハットで上書きされてしまう。
        // これにより FinishShapeshift() 実行時に Hat（=Hats[Default]）が null となり、
        // SetHat(int color) が早期returnして何も描画されなくなる（hat1/visor1が消えるバグの原因）。
        // ProdId 文字列だけでなく、実際の ICosmeticData オブジェクト自体を退避しておき、
        // 復元時に Hats 辞書へ直接書き戻すことで、setOutfit による破壊から守る。
        public ICosmeticData Hat1Data { get; set; }
        public ICosmeticData Visor1Data { get; set; }
    }
}

public class CamouflageButtonAbility : CustomButtonBase, IButtonEffect
{
    private readonly float _coolTime;
    private readonly float _durationTime;
    private readonly CamouflagerAbility _camouflagerAbility;

    public CamouflageButtonAbility(float coolTime, float durationTime, CamouflagerAbility camouflagerAbility)
    {
        _coolTime = coolTime;
        _durationTime = durationTime;
        _camouflagerAbility = camouflagerAbility;
    }

    public override float DefaultTimer => _coolTime;
    public override string buttonText => ModTranslation.GetString("CamouflagerButtonText");
    public override Sprite Sprite => AssetManager.GetAsset<Sprite>("CamouflagerButton.png");
    protected override KeyType keytype => KeyType.Ability1;

    public bool isEffectActive { get; set; }
    public bool effectCancellable => false;
    public float EffectDuration => _durationTime;
    public float EffectTimer { get; set; }
    public Action OnEffectEnds => () =>
    {
        _camouflagerAbility.RpcEndCamouflage();
    };

    public override bool CheckIsAvailable()
    {
        return PlayerControl.LocalPlayer.CanMove && !isEffectActive;
    }

    public override void OnClick()
    {
        _camouflagerAbility.RpcStartCamouflage();
    }

    public bool IsEffectAvailable()
    {
        return true;
    }
}
