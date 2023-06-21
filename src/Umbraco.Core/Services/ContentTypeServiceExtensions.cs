// Copyright (c) Umbraco.
// See LICENSE for more details.

using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.ContentEditing;
using Umbraco.Cms.Core.Services;

namespace Umbraco.Extensions;

public static class ContentTypeServiceExtensions
{
    /// <summary>
    ///     Gets all of the element types (e.g. content types that have been marked as an element type).
    /// </summary>
    /// <param name="contentTypeService">The content type service.</param>
    /// <returns>Returns all the element types.</returns>
    public static IEnumerable<IContentType> GetAllElementTypes(this IContentTypeService contentTypeService)
    {
        if (contentTypeService == null)
        {
            return Enumerable.Empty<IContentType>();
        }

        return contentTypeService.GetAll().Where(x => x.IsElement);
    }

    /// <summary>
    ///     Returns the available composite content types for a given content type
    /// </summary>
    /// <param name="allContentTypes"></param>
    /// <param name="filterContentTypes">
    ///     This is normally an empty list but if additional content type aliases are passed in, any content types containing
    ///     those aliases will be filtered out
    ///     along with any content types that have matching property types that are included in the filtered content types
    /// </param>
    /// <param name="ctService"></param>
    /// <param name="source"></param>
    /// <param name="filterPropertyTypes">
    ///     This is normally an empty list but if additional property type aliases are passed in, any content types that have
    ///     these aliases will be filtered out.
    ///     This is required because in the case of creating/modifying a content type because new property types being added to
    ///     it are not yet persisted so cannot
    ///     be looked up via the db, they need to be passed in.
    /// </param>
    /// <param name="isElement">Whether the composite content types should be applicable for an element type</param>
    /// <returns></returns>
    public static ContentTypeAvailableCompositionsResults GetAvailableCompositeContentTypes(
        this IContentTypeService ctService,
        IContentTypeComposition? source,
        IContentTypeComposition[] allContentTypes,
        string[]? filterContentTypes = null,
        PropertyTypeIdAndCompositionId[]? filterPropertyTypes = null,
        bool isElement = false)
    {
        filterContentTypes = filterContentTypes == null
            ? Array.Empty<string>()
            : filterContentTypes.Where(x => !x.IsNullOrWhiteSpace()).ToArray();

        filterPropertyTypes = filterPropertyTypes == null
            ? Array.Empty<PropertyTypeIdAndCompositionId>()
            : filterPropertyTypes.Where(x => !x.PropertyTypeAlias.IsNullOrWhiteSpace()).ToArray();

        // create the full list of property types to use as the filter
        // this is the combination of all property type aliases found in the content types passed in for the filter
        // as well as the specific property types passed in for the filter
        IEnumerable<IContentTypeComposition> filteredTypes = allContentTypes
            .Where(c => filterContentTypes.InvariantContains(c.Alias));

        var filterTypes = new List<PropertyTypeIdAndCompositionId>();
        foreach (IContentTypeComposition filteredType in filteredTypes)
        {
            var compositionAlias = filteredType.Alias;

            filterTypes.AddRange(
                filteredType.PropertyTypes.
                    Select(s => new PropertyTypeIdAndCompositionId { CompositionAlias = filteredType.Alias, PropertyTypeAlias = s.Alias }));

        }
        filterPropertyTypes = filterTypes.Union(filterPropertyTypes).ToArray();
        //.Select(c => new PropertyTypeIdAndCompositionId { PropertyTypeAlias = c.Alias, CompositionId = c.)
        //.Union(filterPropertyTypes)
        //.ToArray();
        //filterPropertyTypes = 
        var sourceId = source?.Id ?? 0;

        // find out if any content type uses this content type
        IContentTypeComposition[] isUsing =
            allContentTypes.Where(x => x.ContentTypeComposition.Any(y => y.Id == sourceId)).ToArray();
        if (isUsing.Length > 0)
        {
            // if already in use a composition, do not allow any composited types
            return new ContentTypeAvailableCompositionsResults();
        }

        // if it is not used then composition is possible
        // hashset guarantees uniqueness on Id
        var list = new HashSet<IContentTypeComposition>(new DelegateEqualityComparer<IContentTypeComposition>(
            (x, y) => x?.Id == y?.Id,
            x => x.Id));

        // usable types are those that are top-level
        // do not allow element types to be composed by non-element types as this will break the model generation in ModelsBuilder
        IContentTypeComposition[] usableContentTypes = allContentTypes
            .Where(x => x.ContentTypeComposition.Any() == false && (isElement == false || x.IsElement)).ToArray();
        foreach (IContentTypeComposition x in usableContentTypes)
        {
            list.Add(x);
        }

        // indirect types are those that we use, directly or indirectly
        IContentTypeComposition[] indirectContentTypes = GetDirectOrIndirect(source).ToArray();
        foreach (IContentTypeComposition x in indirectContentTypes)
        {
            list.Add(x);
        }

        // At this point we have a list of content types that 'could' be compositions

        // now we'll filter this list based on the filters requested
        var filtered = list
            .Where(x =>
            {
                // need to filter any content types that are included in this list
                return filterContentTypes.Any(c => c.InvariantEquals(x.Alias)) == false;
            })
            .Where(x =>
            {
                // need to filter any content types that have matching property aliases that are included in this list
                // ensure that we don't return if there's any overlapping property aliases from the filtered ones specified
                return filterPropertyTypes.Select(s => s.PropertyTypeAlias).Intersect(
                    x.PropertyTypes.Select(p => p.Alias),
                    StringComparer.InvariantCultureIgnoreCase).Any() == false;
            })
            .OrderBy(x => x.Name)
            .ToList();

        // get ancestor ids - we will filter all ancestors
        IContentTypeComposition[] ancestors = GetAncestors(source, allContentTypes);
        var ancestorIds = ancestors.Select(x => x.Id).ToArray();

        // get filtered compositions that are switchable, by containing a complete set of matching properties with another composition
        var switchableCompositions = new Dictionary<string,IContentTypeComposition>();

        var distinctFilteredCompositionIds = filterPropertyTypes.Where(s => s.CompositionId > 0).Select(s =>s.CompositionId).Distinct();

        foreach (int compositionId in distinctFilteredCompositionIds)
        {
            var applicableProperties = filterPropertyTypes.Where(x => x.CompositionId == compositionId).Select(s => s.PropertyTypeAlias);
            // now we have a series of aliases, and we want to see if we can find any -other- composition that contains that entire series
            var applicableCompositions = list.Where(x => x.Id != compositionId &&
                x.PropertyTypes.Select(s => s.Alias).Intersect(applicableProperties).Count() >= applicableProperties.Count());
            // todo - compare the property editors that we don't just match on alias
            foreach (var composition in applicableCompositions)
            {
                string alias = list.First(f => f.Id == compositionId).Alias;
                switchableCompositions.Add(alias, composition);
            }

        }


        // now we can create our result based on what is still available and the ancestors
        var result = list

            // not itself
            .Where(x => x.Id != sourceId)
            .OrderBy(x => x.Name)
            .Select(
                composition => filtered.Contains(composition) && !switchableCompositions.Values.Contains(composition)
                ? new ContentTypeAvailableCompositionsResult(composition, ancestorIds.Contains(composition.Id) == false, new string[0], false)
                : switchableCompositions.Values.Contains(composition)
                ? new ContentTypeAvailableCompositionsResult(composition, ancestorIds.Contains(composition.Id) == false, switchableCompositions.Where(d => d.Value == composition).Select(s => s.Key).ToArray(), true)
                : new ContentTypeAvailableCompositionsResult(composition, false, new string[0], false)).ToList();

        return new ContentTypeAvailableCompositionsResults(ancestors, result);
    }

    private static IContentTypeComposition[] GetAncestors(
        IContentTypeComposition? ctype,
        IContentTypeComposition[] allContentTypes)
    {
        if (ctype == null)
        {
            return new IContentTypeComposition[] { };
        }

        var ancestors = new List<IContentTypeComposition>();
        var parentId = ctype.ParentId;
        while (parentId > 0)
        {
            IContentTypeComposition? parent = allContentTypes.FirstOrDefault(x => x.Id == parentId);
            if (parent != null)
            {
                ancestors.Add(parent);
                parentId = parent.ParentId;
            }
            else
            {
                parentId = -1;
            }
        }

        return ancestors.ToArray();
    }

    /// <summary>
    ///     Get those that we use directly
    /// </summary>
    /// <param name="ctype"></param>
    /// <returns></returns>
    private static IEnumerable<IContentTypeComposition> GetDirectOrIndirect(IContentTypeComposition? ctype)
    {
        if (ctype == null)
        {
            return Enumerable.Empty<IContentTypeComposition>();
        }

        // hashset guarantees uniqueness on Id
        var all = new HashSet<IContentTypeComposition>(new DelegateEqualityComparer<IContentTypeComposition>(
            (x, y) => x?.Id == y?.Id,
            x => x.Id));

        var stack = new Stack<IContentTypeComposition>();

        foreach (IContentTypeComposition x in ctype.ContentTypeComposition)
        {
            stack.Push(x);
        }

        while (stack.Count > 0)
        {
            IContentTypeComposition x = stack.Pop();
            all.Add(x);
            foreach (IContentTypeComposition y in x.ContentTypeComposition)
            {
                stack.Push(y);
            }
        }

        return all;
    }
}
