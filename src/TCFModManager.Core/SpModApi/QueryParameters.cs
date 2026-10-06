namespace TCFModManager.Core.SpModApi;

// Builds the shared "filter[x]", "fields", "include", "sort", "page" and "per_page" query parameters.
public abstract class QueryParameters : IEnumerable<KeyValuePair<string, string?>>
{
    public string? Fields { get; set; }
    public string? Include { get; set; }
    public string? Sort { get; set; }
    public int? Page { get; set; }
    public int? PerPage { get; set; }

    protected static string? BoolParam(bool? value) => value is null ? null : value.Value ? "true" : "false";

    // Yields the endpoint's query parameters, including the shared paging/shaping ones.
    public virtual IEnumerable<KeyValuePair<string, string?>> ToParameters()
    {
        if (Fields is not null) yield return new("fields", Fields);
        if (Include is not null) yield return new("include", Include);
        if (Sort is not null) yield return new("sort", Sort);
        if (Page is not null) yield return new("page", Page.Value.ToString());
        if (PerPage is not null) yield return new("per_page", PerPage.Value.ToString());
    }

    public IEnumerator<KeyValuePair<string, string?>> GetEnumerator() => ToParameters().GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class ModsQuery : QueryParameters
{
    public string? FilterId { get; set; }
    public string? FilterHubId { get; set; }
    public string? FilterGuid { get; set; }
    public string? FilterName { get; set; }
    public string? FilterSlug { get; set; }
    public string? FilterTeaser { get; set; }
    public bool? FilterFeatured { get; set; }
    public bool? FilterContainsAds { get; set; }
    // Fork (1.3.0): filter[contains_ai_content] is gone - sp-mod.com answers it with 400 now.
    public string? FilterCategoryId { get; set; }
    public string? FilterCategorySlug { get; set; }
    public string? FilterCreatedBetween { get; set; }
    public string? FilterUpdatedBetween { get; set; }
    public string? FilterPublishedBetween { get; set; }
    public string? FilterSptVersion { get; set; }
    public bool? FilterFikaCompatibility { get; set; }
    public bool? FilterIncludeLegacy { get; set; }

    // Free-text search across name, slug, and description.
    public string? SearchQuery { get; set; }

    public override IEnumerable<KeyValuePair<string, string?>> ToParameters()
    {
        if (FilterId is not null) yield return new("filter[id]", FilterId);
        if (FilterHubId is not null) yield return new("filter[hub_id]", FilterHubId);
        if (FilterGuid is not null) yield return new("filter[guid]", FilterGuid);
        if (FilterName is not null) yield return new("filter[name]", FilterName);
        if (FilterSlug is not null) yield return new("filter[slug]", FilterSlug);
        if (FilterTeaser is not null) yield return new("filter[teaser]", FilterTeaser);
        if (FilterFeatured is not null) yield return new("filter[featured]", BoolParam(FilterFeatured));
        if (FilterContainsAds is not null) yield return new("filter[contains_ads]", BoolParam(FilterContainsAds));
        if (FilterCategoryId is not null) yield return new("filter[category_id]", FilterCategoryId);
        if (FilterCategorySlug is not null) yield return new("filter[category_slug]", FilterCategorySlug);
        if (FilterCreatedBetween is not null) yield return new("filter[created_between]", FilterCreatedBetween);
        if (FilterUpdatedBetween is not null) yield return new("filter[updated_between]", FilterUpdatedBetween);
        if (FilterPublishedBetween is not null) yield return new("filter[published_between]", FilterPublishedBetween);
        if (FilterSptVersion is not null) yield return new("filter[spt_version]", FilterSptVersion);
        if (FilterFikaCompatibility is not null) yield return new("filter[fika_compatibility]", BoolParam(FilterFikaCompatibility));
        if (FilterIncludeLegacy is not null) yield return new("filter[include_legacy]", BoolParam(FilterIncludeLegacy));
        if (SearchQuery is not null) yield return new("query", SearchQuery);
        foreach (var kv in base.ToParameters()) yield return kv;
    }
}

public sealed class ModVersionsQuery : QueryParameters
{
    public string? FilterId { get; set; }
    public string? FilterHubId { get; set; }

    // SemVer constraint, e.g. "^1.0.0".
    public string? FilterVersion { get; set; }

    public string? FilterDescription { get; set; }
    public string? FilterPublishedBetween { get; set; }
    public string? FilterCreatedBetween { get; set; }
    public string? FilterUpdatedBetween { get; set; }
    public string? FilterSptVersion { get; set; }

    // Comma-separated: compatible, incompatible, unknown.
    public string? FilterFikaCompatibility { get; set; }

    public override IEnumerable<KeyValuePair<string, string?>> ToParameters()
    {
        if (FilterId is not null) yield return new("filter[id]", FilterId);
        if (FilterHubId is not null) yield return new("filter[hub_id]", FilterHubId);
        if (FilterVersion is not null) yield return new("filter[version]", FilterVersion);
        if (FilterDescription is not null) yield return new("filter[description]", FilterDescription);
        if (FilterPublishedBetween is not null) yield return new("filter[published_between]", FilterPublishedBetween);
        if (FilterCreatedBetween is not null) yield return new("filter[created_between]", FilterCreatedBetween);
        if (FilterUpdatedBetween is not null) yield return new("filter[updated_between]", FilterUpdatedBetween);
        if (FilterSptVersion is not null) yield return new("filter[spt_version]", FilterSptVersion);
        if (FilterFikaCompatibility is not null) yield return new("filter[fika_compatibility]", FilterFikaCompatibility);
        foreach (var kv in base.ToParameters()) yield return kv;
    }
}

public sealed class AddonsQuery : QueryParameters
{
    public string? FilterId { get; set; }
    public string? FilterName { get; set; }
    public string? FilterSlug { get; set; }
    public string? FilterTeaser { get; set; }
    public string? FilterModId { get; set; }
    public bool? FilterContainsAds { get; set; }
    public bool? FilterIsDetached { get; set; }
    public string? FilterCreatedBetween { get; set; }
    public string? FilterUpdatedBetween { get; set; }
    public string? FilterPublishedBetween { get; set; }
    public string? SearchQuery { get; set; }

    public override IEnumerable<KeyValuePair<string, string?>> ToParameters()
    {
        if (FilterId is not null) yield return new("filter[id]", FilterId);
        if (FilterName is not null) yield return new("filter[name]", FilterName);
        if (FilterSlug is not null) yield return new("filter[slug]", FilterSlug);
        if (FilterTeaser is not null) yield return new("filter[teaser]", FilterTeaser);
        if (FilterModId is not null) yield return new("filter[mod_id]", FilterModId);
        if (FilterContainsAds is not null) yield return new("filter[contains_ads]", BoolParam(FilterContainsAds));
        if (FilterIsDetached is not null) yield return new("filter[is_detached]", BoolParam(FilterIsDetached));
        if (FilterCreatedBetween is not null) yield return new("filter[created_between]", FilterCreatedBetween);
        if (FilterUpdatedBetween is not null) yield return new("filter[updated_between]", FilterUpdatedBetween);
        if (FilterPublishedBetween is not null) yield return new("filter[published_between]", FilterPublishedBetween);
        if (SearchQuery is not null) yield return new("query", SearchQuery);
        foreach (var kv in base.ToParameters()) yield return kv;
    }
}

public sealed class AddonVersionsQuery : QueryParameters
{
    public string? FilterId { get; set; }
    public string? FilterVersion { get; set; }
    public string? FilterDescription { get; set; }
    public string? FilterPublishedBetween { get; set; }
    public string? FilterCreatedBetween { get; set; }
    public string? FilterUpdatedBetween { get; set; }

    public override IEnumerable<KeyValuePair<string, string?>> ToParameters()
    {
        if (FilterId is not null) yield return new("filter[id]", FilterId);
        if (FilterVersion is not null) yield return new("filter[version]", FilterVersion);
        if (FilterDescription is not null) yield return new("filter[description]", FilterDescription);
        if (FilterPublishedBetween is not null) yield return new("filter[published_between]", FilterPublishedBetween);
        if (FilterCreatedBetween is not null) yield return new("filter[created_between]", FilterCreatedBetween);
        if (FilterUpdatedBetween is not null) yield return new("filter[updated_between]", FilterUpdatedBetween);
        foreach (var kv in base.ToParameters()) yield return kv;
    }
}

public sealed class ModCategoriesQuery : QueryParameters
{
    public string? FilterId { get; set; }
    public string? FilterSlug { get; set; }
    public string? FilterTitle { get; set; }

    public override IEnumerable<KeyValuePair<string, string?>> ToParameters()
    {
        if (FilterId is not null) yield return new("filter[id]", FilterId);
        if (FilterSlug is not null) yield return new("filter[slug]", FilterSlug);
        if (FilterTitle is not null) yield return new("filter[title]", FilterTitle);
        foreach (var kv in base.ToParameters()) yield return kv;
    }
}

public sealed class SptVersionsQuery : QueryParameters
{
    public string? FilterId { get; set; }
    public string? FilterCreatedBetween { get; set; }
    public string? FilterUpdatedBetween { get; set; }
    public string? FilterSptVersion { get; set; }

    public override IEnumerable<KeyValuePair<string, string?>> ToParameters()
    {
        if (FilterId is not null) yield return new("filter[id]", FilterId);
        if (FilterCreatedBetween is not null) yield return new("filter[created_between]", FilterCreatedBetween);
        if (FilterUpdatedBetween is not null) yield return new("filter[updated_between]", FilterUpdatedBetween);
        if (FilterSptVersion is not null) yield return new("filter[spt_version]", FilterSptVersion);
        foreach (var kv in base.ToParameters()) yield return kv;
    }
}
