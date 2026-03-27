using System;
using UnityEngine;

[DisallowMultipleComponent]
public class NPCIdentityRandomProfile : MonoBehaviour
{
    [Serializable]
    public struct NPCInfoData
    {
        public string displayName;
        public int age;
        public string origin;
        public string talent;
        public string trait;
    }

    [Header("Generated Info")]
    [SerializeField] private bool generateOnAwake = true;
    [SerializeField] private int minAge = 18;
    [SerializeField] private int maxAge = 62;

    [SerializeField] private string displayNameValue;
    [SerializeField] private int ageValue;
    [SerializeField] private string originValue;
    [SerializeField] private string talentValue;
    [SerializeField] private string traitValue;

    [Header("Pools")]
    [SerializeField] private string[] firstNames =
    {
        "Alrik","Borin","Cedric","Darin","Elias","Falk","Gero","Henrik","Ivo","Joran",
        "Kara","Lina","Mira","Nora","Oda","Rhea","Sina","Thea","Vera","Yara"
    };

    [SerializeField] private string[] origins =
    {
        "vom Flusstal","aus dem Nordwald","vom Grenzhof","aus den Hügeln","vom alten Markt",
        "aus dem Süddorf","vom Steinbruch","von der Waldkante","vom Seeweg","aus der Oberstadt"
    };

    [SerializeField] private string[] talents =
    {
        "arbeitet schnell","trägt viel","bleibt ruhig","lernt zügig","hat ein gutes Auge",
        "ist ausdauernd","kann gut organisieren","ist geschickt mit Werkzeug","arbeitet sauber","ist belastbar"
    };

    [SerializeField] private string[] traits =
    {
        "freundlich","stolz","vorsichtig","ehrgeizig","geduldig",
        "stillschweigend","humorvoll","verlässlich","neugierig","stur"
    };

    public string DisplayName => string.IsNullOrWhiteSpace(displayNameValue) ? gameObject.name : displayNameValue;
    public int Age => ageValue;
    public string Origin => originValue;
    public string Talent => talentValue;
    public string Trait => traitValue;

    private void Awake()
    {
        if (generateOnAwake && string.IsNullOrWhiteSpace(displayNameValue))
        {
            GenerateRandomProfile();
        }
    }

    [ContextMenu("Generate Random Profile")]
    public void GenerateRandomProfile()
    {
        displayNameValue = Pick(firstNames, "Bewohner");
        ageValue = UnityEngine.Random.Range(minAge, maxAge + 1);
        originValue = Pick(origins, "unbekannt");
        talentValue = Pick(talents, "solide");
        traitValue = Pick(traits, "ruhig");
    }

    public NPCInfoData GetInfo()
    {
        return new NPCInfoData
        {
            displayName = DisplayName,
            age = Age,
            origin = string.IsNullOrWhiteSpace(Origin) ? "unbekannt" : Origin,
            talent = string.IsNullOrWhiteSpace(Talent) ? "-" : Talent,
            trait = string.IsNullOrWhiteSpace(Trait) ? "-" : Trait
        };
    }

    private string Pick(string[] pool, string fallback)
    {
        if (pool == null || pool.Length == 0) return fallback;
        return pool[UnityEngine.Random.Range(0, pool.Length)];
    }
}
