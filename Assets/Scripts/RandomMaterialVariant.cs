using UnityEngine;

// Da mettere sul prefab di un oggetto che esiste in più colori con lo stesso identico modello:
// alla nascita ne sorteggia uno e lo applica a tutte le mesh dell'oggetto. Nato per lo
// SpeedUpPowerUpAmmo, che è lo stesso teschio in blu, verde o rosa — così ogni munizione che
// compare in partita è diversa dalle altre senza dover tenere tre prefab uguali in tutto tranne
// che nel materiale, e senza che lo spawner debba sapere niente delle varianti.
//
// Il materiale si assegna com'è (sharedMaterial) e non con .material: quest'ultimo ne creerebbe
// una copia per ogni esemplare che nasce, sprecata visto che qui il materiale non va modificato,
// solo scelto fra quelli già pronti.
public class RandomMaterialVariant : MonoBehaviour
{
    [Tooltip("Le varianti fra cui sorteggiare: i materiali dello stesso modello, uno per colore. Con la lista vuota non succede niente e l'oggetto tiene il materiale che ha già.")]
    [SerializeField] private Material[] variants;

    void Awake()
    {
        if (variants == null || variants.Length == 0)
        {
            return;
        }

        Material chosen = variants[Random.Range(0, variants.Length)];
        if (chosen == null)
        {
            return;
        }

        // Tutte le mesh dell'oggetto, anche quelle disattivate: il modello può essere fatto di
        // più pezzi, e devono cambiare colore insieme o verrebbe fuori un teschio a chiazze.
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            renderer.sharedMaterial = chosen;
        }
    }
}
