using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Toolbar : MonoBehaviour {

    public UIItemSlot[] slots;
    public RectTransform highlight;
    public Player player;
    public int slotIndex = 0;

    private InputSystem input;

    private void Awake() {

        input = new InputSystem();

        input.Player.NextItemToolbelt.performed += _ => ScrollSlot(-1);
        input.Player.PreviousItemToolbelt.performed += _ => ScrollSlot(1);

        input.Player.SelectSlot1.performed += _ => SetSlot(0);
        input.Player.SelectSlot2.performed += _ => SetSlot(1);
        input.Player.SelectSlot3.performed += _ => SetSlot(2);
        input.Player.SelectSlot4.performed += _ => SetSlot(3);
        input.Player.SelectSlot5.performed += _ => SetSlot(4);
        input.Player.SelectSlot6.performed += _ => SetSlot(5);
        input.Player.SelectSlot7.performed += _ => SetSlot(6);
        input.Player.SelectSlot8.performed += _ => SetSlot(7);
        input.Player.SelectSlot9.performed += _ => SetSlot(8);

    }

    private void OnEnable() => input.Enable();
    private void OnDisable() => input.Disable();

    private void Start() {

        byte index = 1;
        foreach (UIItemSlot s in slots) {
            
            ItemStack stack = new ItemStack(index, Random.Range(2, 65));
            ItemSlot slot = new ItemSlot(s, stack);
            index++;
        }

        UpdateHighlight();

    }

    private void ScrollSlot(int direction) {

        slotIndex += direction;

        if (slotIndex > slots.Length - 1)
            slotIndex = 0;
        if (slotIndex < 0)
            slotIndex = slots.Length - 1;

        UpdateHighlight();

    }

    private void SetSlot(int index) {

        if (index >= slots.Length) return;

        slotIndex = index;
        UpdateHighlight();

    }

    private void UpdateHighlight() {

        highlight.position = slots[slotIndex].transform.position;

    }

}