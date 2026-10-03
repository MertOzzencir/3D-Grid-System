using System;
using System.Collections.Generic;
using UnityEngine;

// Kayıt ve yükleme. Sahnede bir tane; her sahne kendi slotunu ve neyi kaydedeceğini seçer
// (oyun sahnesi her şeyi, blueprint sahnesi sadece base'leri).
// Kaydet: F5 ve oyun kapanırken. Yükle: oyun başlarken (Start: grid ve diğer yöneticiler hazır).
public class SaveManager : MonoBehaviour
{
    private const int CurrentVersion = 2; // 2: gün saati (dayTime)

    [SerializeField] private SaveRegistrySO registry;
    [Tooltip("Kayıt dosyasının adı (slot). Her sahne kendi dosyasını kullanır.")]
    [SerializeField] private string slotName = "save";

    [Header("Neler kaydedilsin")]
    [SerializeField] private bool saveBases = true;
    [SerializeField] private bool savePlaceables = true;
    [SerializeField] private bool saveInventory = true;

    // Kayıt dosyası okunamadıysa bu oturumda üzerine yazma: bozuk da olsa içindeki veri kaybolmasın
    private bool canSave = true;

    private void Start() => Load();

    private void OnEnable() => InputManager.OnF5 += Save;
    private void OnDisable() => InputManager.OnF5 -= Save;
    private void OnApplicationQuit() => Save();

    // ---------- Kaydet: dünya → tarif → dosya ----------

    public void Save()
    {
        if (!canSave || registry == null)
        {
            Debug.LogWarning(registry == null ? "SaveManager: Registry atanmamış, kaydedilmedi." : "SaveManager: kayıt dosyası okunamamıştı, üzerine yazılmadı.", this);
            return;
        }

        var file = new SaveFile { version = CurrentVersion };

        if (saveBases)
            foreach (GridBase entity in GridManager.Instance.GetAllBases())
                TryAddEntity(file.bases, entity);

        if (savePlaceables)
            foreach (GridPlaceable entity in GridManager.Instance.GetAllPlaceables())
                TryAddEntity(file.placeables, entity);

        if (saveInventory && InventoryManager.Instance != null)
            foreach (var pair in InventoryManager.Instance.sources)
                file.inventory.Add(new ItemSave(pair.Key.SaveId, pair.Value));

        if (DayCycle.Instance != null) file.dayTime = DayCycle.Instance.Time01;

        SaveStorage.Write(slotName, JsonUtility.ToJson(file, true));
        Debug.Log($"Kaydedildi: {SaveStorage.PathFor(slotName)} ({file.bases.Count} base, {file.placeables.Count} placeable)", this);
    }

    private void TryAddEntity(List<EntitySave> list, GridEntity entity)
    {
        GridEntitySOBase data = entity.GetData();
        if (!registry.Contains(data))
        {
            Debug.LogWarning($"{entity.name} kaydedilemedi: SO'su SaveRegistry'de yok (Registry → Find All Assets)", entity);
            return;
        }

        string state = entity is ISaveState saveState ? saveState.CaptureState() : null;
        list.Add(new EntitySave(data.SaveId, entity.OriginWorldPosition, entity.Rotation, state));
    }

    // ---------- Yükle: dosya → tarif → dünya ----------

    public void Load()
    {
        if (registry == null)
        {
            Debug.LogWarning("SaveManager: Registry atanmamış, yüklenmedi.", this);
            return;
        }
        if (!SaveStorage.TryRead(slotName, out string json)) return; // ilk açılış: kayıt yok

        SaveFile file;
        try
        {
            file = JsonUtility.FromJson<SaveFile>(json);
        }
        catch (Exception e)
        {
            canSave = false;
            Debug.LogError($"Kayıt dosyası okunamadı, boş dünyayla başlanıyor: {SaveStorage.PathFor(slotName)}\n{e.Message}", this);
            return;
        }

        // Base'ler önce: placeable'lar onların üstünde durur
        if (saveBases)
            foreach (EntitySave save in file.bases)
                Spawn<GridBase>(save, GridManager.Instance.PlaceBase);

        if (savePlaceables)
            foreach (EntitySave save in file.placeables)
                Spawn<GridPlaceable>(save, GridManager.Instance.PlaceablePlaceOn);

        if (file.dayTime >= 0f && DayCycle.Instance != null) DayCycle.Instance.Time01 = file.dayTime;

        if (saveInventory && InventoryManager.Instance != null)
            LoadInventory(file.inventory);
    }

    private void Spawn<T>(EntitySave save, Func<T, Vector3, bool> place) where T : GridEntity
    {
        GridEntitySOBase data = registry.GetEntity(save.id);
        if (data == null || !(data.GetPrefabBase() is T prefab))
        {
            Debug.LogWarning($"Kayıttaki '{save.id}' bulunamadı, atlandı.", this);
            return;
        }

        T entity = Instantiate(prefab);
        entity.Rotation = save.rotation; // footprint rotasyona bağlı: yerleştirmeden önce

        if (entity is ISaveState saveState && !string.IsNullOrEmpty(save.state))
            saveState.RestoreState(save.state);

        if (!place(entity, save.Position))
        {
            Debug.LogWarning($"'{save.id}' {save.Position} hücresine yerleştirilemedi, atlandı.", this);
            Destroy(entity.gameObject);
        }
    }

    private void LoadInventory(List<ItemSave> items)
    {
        var sources = new Dictionary<SourcesSO, int>();
        foreach (ItemSave item in items)
        {
            SourcesSO source = registry.GetSource(item.id);
            if (source != null) sources[source] = item.amount;
            else Debug.LogWarning($"Kayıttaki kaynak '{item.id}' bulunamadı, atlandı.", this);
        }
        InventoryManager.Instance.LoadSources(sources);
    }

    [ContextMenu("Kaydı Sil")]
    private void DeleteSave()
    {
        SaveStorage.Delete(slotName);
        Debug.Log($"Silindi: {SaveStorage.PathFor(slotName)}", this);
    }
}
