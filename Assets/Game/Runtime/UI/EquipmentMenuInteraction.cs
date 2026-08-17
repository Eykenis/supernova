using UnityEngine;
using UnityEngine.EventSystems;

namespace Supernova.UI
{
    /// <summary>
    /// Routes UGUI drag gestures to the equipment menu without coupling item data
    /// to the generated view hierarchy.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EquipmentMenuInteraction : MonoBehaviour,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IDropHandler
    {
        private enum InteractionRole
        {
            None,
            OwnedItem,
            EquipmentSlot,
        }

        [SerializeField] private InteractionRole role;
        [SerializeField] private int index = -1;

        private EquipmentLoadoutMenu menu;
        private bool dragAccepted;

        public bool IsOwnedItemSource => role == InteractionRole.OwnedItem;
        public bool IsEquipmentSlotTarget => role == InteractionRole.EquipmentSlot;
        public int Index => index;

        public void ConfigureOwnedItem(
            EquipmentLoadoutMenu configuredMenu,
            int cellIndex)
        {
            menu = configuredMenu;
            role = InteractionRole.OwnedItem;
            index = cellIndex;
        }

        public void ConfigureEquipmentSlot(
            EquipmentLoadoutMenu configuredMenu,
            int slotIndex)
        {
            menu = configuredMenu;
            role = InteractionRole.EquipmentSlot;
            index = slotIndex;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            dragAccepted = false;
            if (menu == null)
                return;

            if (role == InteractionRole.OwnedItem)
            {
                dragAccepted = menu.BeginOwnedItemDrag(index, eventData.position);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragAccepted || menu == null)
                return;

            if (role == InteractionRole.OwnedItem)
                menu.UpdateOwnedItemDrag(eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (dragAccepted && menu != null
                && role == InteractionRole.OwnedItem)
            {
                menu.EndOwnedItemDrag();
            }
            dragAccepted = false;
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (menu != null && role == InteractionRole.EquipmentSlot)
                menu.DropDraggedItemOnSlot(index);
        }
    }
}
