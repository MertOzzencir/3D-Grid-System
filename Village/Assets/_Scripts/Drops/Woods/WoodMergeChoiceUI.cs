using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;
using TMPro;

public class WoodMergeChoiceUI : MonoBehaviour
{
    [SerializeField] private GameObject menuRoot;
    [SerializeField] private Button[] buttons;
    [SerializeField] private TextMeshProUGUI[] buttonLabels; // opsiyonel, hangi tarif olduğunu göstermek için

    void OnEnable()
    {
        WoodMergeRecipeManager.OnMergeChoiceRequested += ShowMenu;
        WoodMergeRecipeManager.OnMergeChoiceCancelled += HideMenu;
    }

    void OnDisable()
    {
        WoodMergeRecipeManager.OnMergeChoiceRequested -= ShowMenu;
        WoodMergeRecipeManager.OnMergeChoiceCancelled -= HideMenu;
    }

    private void ShowMenu(Wood target, Wood carried, List<WoodMergeRecipe> matches, Vector3 worldPos)
    {
        menuRoot.SetActive(true);
        transform.position = worldPos;

        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].onClick.RemoveAllListeners();

            if (i < matches.Count)
            {
                WoodMergeRecipe recipe = matches[i];

                buttons[i].gameObject.SetActive(true);
                if (buttonLabels != null && i < buttonLabels.Length)
                    buttonLabels[i].text = recipe.Orientation.ToString();

                buttons[i].onClick.AddListener(() =>
                {
                    WoodMergeRecipeManager.ApplyRecipe(target, carried, recipe);
                    HideMenu();
                });
            }
            else
            {
                buttons[i].gameObject.SetActive(false); 
            }
        }
    }

    private void HideMenu()
    {
        menuRoot.SetActive(false);
    }
}