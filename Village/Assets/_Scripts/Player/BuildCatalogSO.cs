using System;
using System.Collections.Generic;
using UnityEngine;

// Oyuncunun kurabileceği yapıların listesi (PlayerBuildMenu bunu gösterir). Geliştirici build mode'unun menülerinden
// ayrı: orası oyunu kurmak için, bu oyuncunun oyun içinde kurdukları (örn. insan kafesleri). Her kategori alt barda bir
// sekme olur, içindeki yapılar kart olur. Kartın adı ve ikonu yapının verisinden (Name, Icon) gelir.
[CreateAssetMenu(fileName = "Build Catalog", menuName = "Village/Build Catalog")]
public class BuildCatalogSO : ScriptableObject
{
    [Serializable]
    public class Category
    {
        [Tooltip("Sekmede yazan ad (oyuncu görür, İngilizce)")]
        public string Name = "Cages";
        [Tooltip("Bu sekmedeki yapıların verileri (GridPlaceable SO'ları, örn. blueprint'ler)")]
        public List<GridEntitySOBase> Items = new List<GridEntitySOBase>();
    }

    public List<Category> Categories = new List<Category>();
}
