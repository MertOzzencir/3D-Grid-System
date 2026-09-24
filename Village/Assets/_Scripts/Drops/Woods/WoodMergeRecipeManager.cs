using System;
using System.Collections.Generic;
using UnityEngine;

public class WoodMergeRecipeManager : MonoBehaviour
{
    [SerializeField] private WoodMergeRecipe[] recipeEntries;

    public static WoodMergeRecipe[] Recipes;

    public static event Action<Wood, Wood, List<WoodMergeRecipe>, Vector3> OnMergeChoiceRequested;
    public static event Action OnMergeChoiceCancelled;

    void Awake()
    {
        Recipes = recipeEntries;
    }

    public static List<WoodMergeRecipe> GetMatchingRecipes(Lenghts a, Lenghts b)
    {
        List<WoodMergeRecipe> matches = new List<WoodMergeRecipe>();

        foreach (var r in Recipes)
        {
            bool sameOrder = r.InputA == a && r.InputB == b;
            bool reversedOrder = r.InputA == b && r.InputB == a;

            if (sameOrder || reversedOrder)
                matches.Add(r);
        }

        return matches;
    }

    public static void RequestMergeChoice(Wood target, Wood carried, List<WoodMergeRecipe> matches, Vector3 worldPosition)
    {
        OnMergeChoiceRequested?.Invoke(target, carried, matches, worldPosition);
    }

    public static void CancelMergeChoice()
    {
        OnMergeChoiceCancelled?.Invoke();
    }

    public static void ApplyRecipe(Wood target, Wood carried, WoodMergeRecipe recipe)
    {
        Vector3 spawnPosition = target.transform.position;
        GridManager.Instance.PlaceableRemoveOn(target, true);
        Destroy(carried.gameObject);
        InteractableController.Instance.HardCancel(carried);

        Wood result = Instantiate(recipe.ResultPrefab, spawnPosition, Quaternion.identity);
        result.OnSpawned();
        GridManager.Instance.PlaceablePlaceOn(result, spawnPosition);
    }
    public static bool TryMerge(Wood target, IInteractable tool)
    {
        if (tool is not Wood carried) return false;

        List<WoodMergeRecipe> matches = GetMatchingRecipes(target.Length, carried.Length);

        if (matches.Count == 0) return false;

        if (matches.Count == 1)
        {
            ApplyRecipe(target, carried, matches[0]);
            return true;
        }

        RequestMergeChoice(target, carried, matches, target.transform.position);
        return true;
    }
}