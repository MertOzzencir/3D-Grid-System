using System.Collections.Generic;
using UnityEngine;

// Yan katmandan birleşmiş odun: base odun + katmanlarındaki parçalar.
// Tekrar birleşme hedefi olamaz ama taşınabilir ve döndürülebilir.
public class MergedWood : GridPlaceable, IInteractable, IBlueprintPiece
{
    private const float ColliderThickness = 0.75f;

    [SerializeField] private float followSpeed = 10f;

    // Katman listesi: index = katman (WoodLayout ile aynı numaralama), değer = o katmandaki odun uzunluğu, 0 = boş
    [SerializeField] private int baseLength;
    [SerializeField] private List<int> layers = new List<int>();

    private GridDragMotor drag;
    private GridFootprint[] footprints; // rotasyon başına bir kez hesaplanır

    public int BaseLength => baseLength;
    public IReadOnlyList<int> Layers => layers;

    // Örn. 2BR base, katman 1'de 1BR → "MergedWood:2:0,1,0"
    public string BlueprintSignature => $"MergedWood:{baseLength}:{string.Join(",", layers)}";

    private void Awake() => drag = new GridDragMotor(this, followSpeed);

    public void Build(int baseLength, int layer, int pieceLength, WoodCatalogSO catalog)
    {
        this.baseLength = baseLength;
        layers = new List<int>(new int[WoodLayout.LayerCount(baseLength)]);
        layers[layer] = pieceLength;
        footprints = null;

        AddPiece(catalog.GetWood(baseLength), Vector3Int.zero, Quaternion.identity, baseLength);
        for (int i = 0; i < layers.Count; i++)
        {
            if (layers[i] == 0) continue;
            AddPiece(catalog.GetWood(layers[i]), WoodLayout.PieceStart(baseLength, i), WoodLayout.PieceRotation(i), layers[i]);
        }
    }

    public override GridFootprint GetFootprint()
    {
        if (footprints == null)
        {
            var cells = new List<Vector3Int>(WoodLayout.BaseCells(baseLength));
            for (int i = 0; i < layers.Count; i++)
                if (layers[i] > 0)
                    cells.AddRange(WoodLayout.PieceCells(baseLength, i, layers[i]));

            footprints = GridMaskRotator.AllRotations(new GridFootprint(cells.ToArray()));
        }
        return footprints[(int)Rotation];
    }

    // Parçanın modelini child olarak ekler, root'a da parçayı kaplayan bir collider koyar
    // (InteractableController collider'ın kendi objesinde IInteractable arıyor).
    private void AddPiece(Wood woodPrefab, Vector3Int start, Quaternion rotation, int length)
    {
        if (woodPrefab == null)
        {
            Debug.LogWarning($"WoodCatalog'da {length} uzunluğunda odun yok", this);
            return;
        }

        var holder = new GameObject($"Piece {length}BR").transform;
        holder.SetParent(transform, false);
        holder.localPosition = start;
        holder.localRotation = rotation;
        Instantiate(woodPrefab.Visual.gameObject, holder, false);

        Vector3 axis = rotation * Vector3.up;
        Vector3 size = rotation * new Vector3(ColliderThickness, length, ColliderThickness);
        var box = gameObject.AddComponent<BoxCollider>();
        box.center = (Vector3)start + axis * (length - 1) / 2f;
        box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
    }

    // --- IInteractable ---
    public void Interact(out bool finished)
    {
        if (!drag.TryUseOnTarget(out finished))
            finished = true;
    }

    public void InteractContractBeginnig() => drag.Begin();
    public void InteractContract(out bool success) { success = true; drag.Tick(); }
    public void ContractCancel() => drag.Cancel();
}
