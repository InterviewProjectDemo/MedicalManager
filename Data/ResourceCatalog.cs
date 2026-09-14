namespace MedicalManager.Data;

public sealed record ResourceLink(string Title, string Url, string Description);

public sealed record ResourceCategory(string Id, string Name, string Summary, string AccentClass, IReadOnlyList<ResourceLink> Links);

public static class ResourceCatalog
{
    public static readonly IReadOnlyList<ResourceCategory> Categories =
    [
        new ResourceCategory(
            "diabetes",
            "Diabetes",
            "Blood sugar, A1C, medications, and living with type 1 or type 2 diabetes.",
            "accent-sugar",
            [
                new ResourceLink("American Diabetes Association", "https://diabetes.org/", "Education, food, and living with diabetes."),
                new ResourceLink("CDC Diabetes", "https://www.cdc.gov/diabetes/", "Prevention, type 2 tools, and public-health guidance."),
                new ResourceLink("NIDDK Diabetes", "https://www.niddk.nih.gov/health-information/diabetes", "NIH research-based patient information."),
                new ResourceLink("MedlinePlus: Diabetes", "https://medlineplus.gov/diabetes.html", "Easy-to-read overviews from the National Library of Medicine."),
                new ResourceLink("Diabetes Food Hub", "https://www.diabetesfoodhub.org/", "ADA recipes and meal planning."),
            ]),
        new ResourceCategory(
            "kidney",
            "Kidney",
            "Chronic kidney disease, labs (eGFR, creatinine), dialysis, and kidney-friendly eating.",
            "accent-bp",
            [
                new ResourceLink("National Kidney Foundation", "https://www.kidney.org/", "CKD stages, symptoms, and support."),
                new ResourceLink("American Kidney Fund", "https://www.kidneyfund.org/", "Patient education and financial assistance resources."),
                new ResourceLink("NIDDK Kidney Disease", "https://www.niddk.nih.gov/health-information/kidney-disease", "NIH guides on CKD, dialysis, and transplant."),
                new ResourceLink("MedlinePlus: Kidney Diseases", "https://medlineplus.gov/kidneydiseases.html", "Trusted library articles and videos."),
                new ResourceLink("NKF: Nutrition and kidney disease", "https://www.kidney.org/kidney-topics/nutrition-and-kidney-disease", "Protein, sodium, potassium, and phosphorus guidance."),
            ]),
        new ResourceCategory(
            "nutrition",
            "Nutrition",
            "Everyday eating patterns, sodium, carbs, and heart-healthy meals.",
            "accent-meds",
            [
                new ResourceLink("MyPlate", "https://www.myplate.gov/", "USDA plate method and food groups."),
                new ResourceLink("Nutrition.gov", "https://www.nutrition.gov/", "Federal nutrition topics and tools."),
                new ResourceLink("Academy of Nutrition and Dietetics", "https://www.eatright.org/", "Find a dietitian and practical food advice."),
                new ResourceLink("American Heart Association: Healthy eating", "https://www.heart.org/en/healthy-living/healthy-eating", "Heart-conscious meals and labels."),
                new ResourceLink("CDC Nutrition", "https://www.cdc.gov/nutrition/", "Healthy eating for prevention and chronic conditions."),
            ]),
    ];

    public static readonly IReadOnlyList<string> SuggestedQuestions =
    [
        "What is a healthy A1C range?",
        "How does chronic kidney disease progress?",
        "What is a kidney-friendly diet?",
        "How do I count carbohydrates?",
        "What foods are high in potassium?",
        "How does metformin work?",
    ];
}
