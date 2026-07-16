using SuperNewRoles.Modules;
using SuperNewRoles.Roles.Ability.CustomButton;
using SuperNewRoles.Roles.Impostor;
using UnityEngine;
using System;

namespace SuperNewRoles.Roles.Ability;

    /// <summary>
    /// クリーナー役職の能力クラス（Vulture に合わせて改良）
    /// </summary>
    public class CleanerAbility : CustomButtonBase
    {
        private float _coolTime;
        private Sprite _sprite;
        private string _buttonText;

        public CleanerAbility(float coolTime)
        {
            _coolTime = coolTime;
            // _sprite = AssetManager.GetAsset<Sprite>("CleanerButton.png");
            _buttonText = ModTranslation.GetString("CleanerButton");
        }

        public override float DefaultTimer => _coolTime;
        public override float Timer { get; set; }
        public override string buttonText => _buttonText;
        public override Sprite Sprite => _sprite;
        protected override KeyType keytype => KeyType.Ability1;

        public override bool CheckIsAvailable()
        {
            // プレイヤーが死んでいない、会議中でない、ベント内でない場合に使用可能
            if (Player.Data.IsDead || MeetingHud.Instance || Player.Player.inVent)
                return false;

            return HasNearbyDeadBody();
        }

        private bool HasNearbyDeadBody()
        {
            Vector2 playerPos = Player.GetTruePosition();
            float radius = Player.MaxReportDistance;

            foreach (Collider2D collider in Physics2D.OverlapCircleAll(playerPos, radius, Constants.PlayersOnlyMask))
            {
                if (collider == null || collider.tag != "DeadBody") continue;

                DeadBody component = collider.GetComponent<DeadBody>();
                if (component == null || component.Reported) continue;

                Vector2 bodyPos = component.TruePosition;
                if (!PhysicsHelpers.AnythingBetween(playerPos, bodyPos, Constants.ShipAndObjectsMask, false))
                {
                    return true;
                }
            }
            return false;
        }

        public override void OnClick()
        {
            Vector2 playerPos = Player.GetTruePosition();
            float radius = Player.MaxReportDistance;

            foreach (Collider2D collider in Physics2D.OverlapCircleAll(playerPos, radius, Constants.PlayersOnlyMask))
            {
                if (collider == null || collider.tag != "DeadBody") continue;

                DeadBody component = collider.GetComponent<DeadBody>();
                if (component == null || component.Reported) continue;

                Vector2 bodyPos = component.TruePosition;
                if (!PhysicsHelpers.AnythingBetween(playerPos, bodyPos, Constants.ShipAndObjectsMask, false))
                {
                    CleanDeadBody(component);
                    break;
                }
            }
        }

        private void CleanDeadBody(DeadBody deadBody)
        {
            // 死体を消す（RPC は親 ID のみ送る）
            RpcCleanDeadBody(deadBody.ParentId);
        }

        [CustomRPC]
        public static void RpcCleanDeadBody(int parentId)
        {
            bool ateOrpheusRitualCorpse = false;
            foreach (DeadBody deadBody in UnityEngine.Object.FindObjectsOfType<DeadBody>())
            {
                if (deadBody.ParentId == parentId)
                {
                    ateOrpheusRitualCorpse |= OrpheusMainAbility.IsManagedCorpseBody(deadBody);
                    GameObject.Destroy(deadBody.gameObject);
                }
            }
            if (ateOrpheusRitualCorpse)
                OrpheusMainAbility.MarkCorpseUnavailableFromExternalUse((byte)parentId);
        }
    }