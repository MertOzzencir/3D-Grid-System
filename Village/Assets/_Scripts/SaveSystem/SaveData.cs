using System;
using System.Collections.Generic;
using UnityEngine;

// Kayıt dosyasının içeriği. Sadece düz değerler (metin, sayı); GameObject ya da referans yok.
// Dünya bu "tarif"ten yeniden kurulur. Yeni alan eklenince CurrentVersion artırılır (SaveManager).
[Serializable]
public class SaveFile
{
    public int version;
    public List<EntitySave> bases = new List<EntitySave>();
    public List<EntitySave> placeables = new List<EntitySave>();
    public List<ItemSave> inventory = new List<ItemSave>();
    public float dayTime = -1f; // gün saati (DayCycle, 0..1); -1 = kayıtta yok (eski kayıt)
}

// Grid'deki bir obje: ne (id), nerede (hücre), hangi yöne (rotasyon), ve gerekiyorsa kendi durumu
[Serializable]
public class EntitySave
{
    public string id;
    public int x, y, z;
    public GridMaskRotator.Rotation rotation;
    public string state; // ISaveState olmayan objelerde boş

    public Vector3Int Position => new Vector3Int(x, y, z);

    public EntitySave() { }

    public EntitySave(string id, Vector3Int position, GridMaskRotator.Rotation rotation, string state)
    {
        this.id = id;
        x = position.x;
        y = position.y;
        z = position.z;
        this.rotation = rotation;
        this.state = state;
    }
}

[Serializable]
public class ItemSave
{
    public string id;
    public int amount;

    public ItemSave() { }

    public ItemSave(string id, int amount)
    {
        this.id = id;
        this.amount = amount;
    }
}
