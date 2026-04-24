using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class Inventory : MonoBehaviour {

    public GameObject slotPrefab;
    public GameObject armorUI;
    World world;

    List<ItemSlot> slots = new List<ItemSlot>();

    private void Start() {

        world = GameObject.Find("World").GetComponent<World>();

        for (int i = 1; i < world.blocktypes.Length; i++) {

            GameObject newSlot = Instantiate(slotPrefab, transform);

            ItemStack stack = new ItemStack((byte)i, 64);
            ItemSlot slot = new ItemSlot(newSlot.GetComponent<UIItemSlot>(), stack);
            slot.isCreative = true;
        }
    }

    private void Update() {

        if (Keyboard.current.eKey.wasPressedThisFrame) {
            armorUI.SetActive(!armorUI.activeSelf);
        }
    }
}
