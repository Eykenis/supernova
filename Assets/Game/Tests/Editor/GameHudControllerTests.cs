using NUnit.Framework;
using Supernova.Gameplay;
using Supernova.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Supernova.Tests
{
    public sealed class GameHudControllerTests
    {
        private GameObject hudObject;
        private GameObject sourceObject;

        [TearDown]
        public void TearDown()
        {
            if (hudObject != null) Object.DestroyImmediate(hudObject);
            if (sourceObject != null) Object.DestroyImmediate(sourceObject);
        }

        [Test]
        public void RebuildDefaultView_CreatesUguiCrosshairAndHealthWidget()
        {
            hudObject = new GameObject("Game HUD");
            GameHudController controller = hudObject.AddComponent<GameHudController>();

            controller.RebuildDefaultView();

            Assert.That(hudObject.GetComponentInChildren<Canvas>(), Is.Not.Null);
            Assert.That(hudObject.GetComponentInChildren<CanvasScaler>(), Is.Not.Null);
            Assert.That(hudObject.transform.Find("Crosshair Canvas/Crosshair/Horizontal")?.GetComponent<Image>(), Is.Not.Null);
            Assert.That(hudObject.transform.Find("Crosshair Canvas/Crosshair/Vertical")?.GetComponent<Image>(), Is.Not.Null);
            Assert.That(hudObject.transform.Find("HUD Canvas/Health Panel"), Is.Not.Null);
            Assert.That(hudObject.transform.Find("HUD Canvas/Health Panel/Track/Fill")?.GetComponent<Image>(), Is.Not.Null);
            Assert.That(hudObject.transform.Find("HUD Canvas/Health Panel/Header/Value")?.GetComponent<TMP_Text>(), Is.Not.Null);
            Transform hotbar = hudObject.transform.Find("HUD Canvas/Hotbar");
            Assert.That(hotbar, Is.Not.Null);
            Assert.That(hotbar.childCount, Is.EqualTo(PlayerInventory.SlotCount));
            Assert.That(hotbar.Find("Slot 1/Item")?.GetComponent<TMP_Text>().text, Is.EqualTo("PICKAXE"));
            Assert.That(hotbar.Find("Slot 2/Item")?.GetComponent<TMP_Text>().text, Is.EqualTo("MAGNET"));
            Assert.That(hotbar.Find("Slot 10/Key")?.GetComponent<TMP_Text>().text, Is.EqualTo("0"));
        }

        [Test]
        public void Presenter_UpdatesHealthTextAndFillPercentage()
        {
            hudObject = new GameObject("Game HUD");
            GameHudController controller = hudObject.AddComponent<GameHudController>();
            controller.RebuildDefaultView();

            GameObject panel = hudObject.transform.Find("HUD Canvas/Health Panel").gameObject;
            RectTransform fill = (RectTransform)hudObject.transform.Find("HUD Canvas/Health Panel/Track/Fill");
            Image fillImage = fill.GetComponent<Image>();
            TMP_Text value = hudObject.transform.Find("HUD Canvas/Health Panel/Header/Value").GetComponent<TMP_Text>();
            var presenter = new GameHudPresenter(panel, fill, fillImage, value);

            presenter.SetHealth(25f, 100f);

            Assert.That(value.text, Is.EqualTo("25 / 100"));
            Assert.That(fill.anchorMax.x, Is.EqualTo(0.25f).Within(0.001f));
        }

        [Test]
        public void InventorySelection_UpdatesHotbarHighlight()
        {
            sourceObject = new GameObject("Player");
            PlayerToolController inventory = sourceObject.AddComponent<PlayerToolController>();
            hudObject = new GameObject("Game HUD");
            GameHudController controller = hudObject.AddComponent<GameHudController>();
            controller.RebuildDefaultView();
            controller.BindInventorySource(inventory);

            inventory.SelectSlot(1);

            Image first = hudObject.transform.Find("HUD Canvas/Hotbar/Slot 1").GetComponent<Image>();
            Image second = hudObject.transform.Find("HUD Canvas/Hotbar/Slot 2").GetComponent<Image>();
            Assert.That(second.color, Is.Not.EqualTo(first.color));
            Assert.That(controller.InventorySource, Is.SameAs(inventory));
        }
    }

    internal sealed class FakeDamageable : MonoBehaviour, IDamageable
    {
        private float current;
        private float maximum;

        public GameObject Owner => gameObject;
        public float CurrentHealth => current;
        public float MaximumHealth => maximum;
        public bool IsAlive => current > 0f;

        public void SetHealth(float currentHealth, float maximumHealth)
        {
            maximum = maximumHealth;
            current = currentHealth;
        }

        public bool ReceiveDamage(in DamageInfo damage)
        {
            current = Mathf.Max(0f, current - damage.Amount);
            return true;
        }
    }
}
