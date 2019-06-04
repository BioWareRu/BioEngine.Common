using System.Linq;
using System.Threading.Tasks;
using BioEngine.Core.Abstractions;
using BioEngine.Core.DB.Queries;
using BioEngine.Core.Entities;
using BioEngine.Core.Users;
using BioEngine.Core.Validation;
using Microsoft.EntityFrameworkCore;

namespace BioEngine.Core.Repository
{
    public abstract class ContentItemRepository<TEntity> : SectionEntityRepository<TEntity>
        where TEntity : ContentItem, IEntity, ISiteEntity, ISectionEntity
    {
        private readonly IUserDataProvider? _userDataProvider;

        protected ContentItemRepository(BioRepositoryContext<TEntity> repositoryContext,
            SectionsRepository sectionsRepository, IUserDataProvider? userDataProvider = null) : base(repositoryContext,
            sectionsRepository)
        {
            _userDataProvider = userDataProvider;
        }

        protected override IQueryable<TEntity> GetBaseQuery(QueryContext<TEntity>? queryContext = null)
        {
            return ApplyContext(DbContext.Set<TEntity>().Include(p => p.Blocks), queryContext);
        }

        protected override void RegisterValidators()
        {
            base.RegisterValidators();
            Validators.Add(new ContentItemValidator<TEntity>(DbContext));
        }

        protected override async Task AfterLoadAsync(TEntity[] entities)
        {
            if (_userDataProvider != null && entities != null && entities.Length > 0)
            {
                await base.AfterLoadAsync(entities);
                var userIds = entities.Select(e => e.AuthorId).Distinct().ToArray();
                var data = await _userDataProvider.GetDataAsync(userIds);
                foreach (var entity in entities)
                {
                    entity.Author = data.FirstOrDefault(d => d.Id == entity.AuthorId);
                }
            }
        }
    }
}
