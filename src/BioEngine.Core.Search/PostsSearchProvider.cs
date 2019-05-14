using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BioEngine.Core.Entities;
using BioEngine.Core.Repository;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;

namespace BioEngine.Core.Search
{
    [UsedImplicitly]
    public class PostsSearchProvider : BaseSearchProvider<Post>
    {
        private readonly TagsRepository _tagsRepository;
        private readonly PostsRepository _postsRepository;

        public PostsSearchProvider(ISearcher searcher, ILogger<BaseSearchProvider<Post>> logger,
            TagsRepository tagsRepository, PostsRepository postsRepository) : base(searcher,
            logger)
        {
            _tagsRepository = tagsRepository;
            _postsRepository = postsRepository;
        }

        protected override async Task<IEnumerable<SearchModel>> GetSearchModelsAsync(IEnumerable<Post> entities)
        {
            var tagIds = entities.SelectMany(e => e.TagIds).Distinct().ToArray();
            var tags = await _tagsRepository.GetByIdsAsync(tagIds);
            return entities.Select(post =>
            {
                var model = new SearchModel(post.Id, post.Title, post.Url, string.Join(" ", post.Blocks.Select(b => b.ToString()).Where(s => !string.IsNullOrEmpty(s))), post.DateAdded)
                {
                    SectionIds = post.SectionIds,
                    AuthorId = post.AuthorId,
                    SiteIds = post.SiteIds,
                    Tags = tags.Where(t => post.TagIds.Contains(t.Id)).Select(t => t.Title).ToArray()
                };

                return model;
            });
        }

        protected override async Task<IEnumerable<Post>> GetEntitiesAsync(IEnumerable<SearchModel> searchModels)
        {
            var ids = searchModels.Select(s => s.Id).Distinct().ToArray();
            return await _postsRepository.GetByIdsAsync(ids);
        }
    }
}