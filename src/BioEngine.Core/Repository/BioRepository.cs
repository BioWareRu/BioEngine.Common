﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BioEngine.Core.Abstractions;
using BioEngine.Core.DB;
using BioEngine.Core.Properties;
using BioEngine.Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;

namespace BioEngine.Core.Repository
{
    [PublicAPI]
    public abstract class BioRepository<TEntity> : IBioRepository<TEntity> where TEntity : class, IEntity
    {
        protected readonly BioContext DbContext;
        protected readonly List<IValidator<TEntity>> Validators;
        protected readonly PropertiesProvider PropertiesProvider;
        public BioRepositoryHooksManager HooksManager { get; set; }

        protected BioRepository(BioRepositoryContext<TEntity> repositoryContext)
        {
            DbContext = repositoryContext.DbContext;
            Validators = repositoryContext.Validators ?? new List<IValidator<TEntity>>();
            PropertiesProvider = repositoryContext.PropertiesProvider;
            HooksManager = repositoryContext.HooksManager;
            Init();
        }


        private void Init()
        {
            RegisterValidators();
        }

        protected virtual void RegisterValidators()
        {
            Validators.Add(new EntityValidator());
        }

        public virtual async Task<(TEntity[] items, int itemsCount)> GetAllAsync(
            Func<IQueryable<TEntity>, IQueryable<TEntity>>? configureQuery = null)
        {
            var itemsCount = await CountAsync(configureQuery);

            var query = ConfigureQuery(GetBaseQuery(), configureQuery);


            var items = await query.ToArrayAsync();
            await AfterLoadAsync(items);

            return (items, itemsCount);
        }

        protected virtual Task AfterLoadAsync(TEntity entity)
        {
            return entity != null ? AfterLoadAsync(new[] {entity}) : Task.CompletedTask;
        }

        protected virtual async Task AfterLoadAsync(TEntity[] entities)
        {
            await PropertiesProvider.LoadPropertiesAsync(entities);
        }

        public virtual async Task<int> CountAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>>? configureQuery = null)
        {
            var query = ConfigureQuery(GetBaseQuery(), configureQuery);

            return await query.CountAsync();
        }

        public virtual async Task<TEntity> GetByIdAsync(Guid id,
            Func<IQueryable<TEntity>, IQueryable<TEntity>>? configureQuery = null)
        {
            var query = ConfigureQuery(GetBaseQuery().Where(i => i.Id.Equals(id)), configureQuery);

            var item = await query.FirstOrDefaultAsync();
            await AfterLoadAsync(item);
            return item;
        }

        public virtual async Task<TEntity> GetAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>> configureQuery)
        {
            var query = ConfigureQuery(GetBaseQuery(), configureQuery);
            var item = await query.FirstOrDefaultAsync();
            await AfterLoadAsync(item);
            return item;
        }


        public virtual async Task<TEntity> NewAsync()
        {
            var item = Activator.CreateInstance<TEntity>();
            await AfterLoadAsync(item);
            return item;
        }

        public virtual async Task<TEntity[]> GetByIdsAsync(Guid[] ids,
            Func<IQueryable<TEntity>, IQueryable<TEntity>>? configureQuery = null)
        {
            var query = GetBaseQuery().Where(i => ids.Contains(i.Id));
            var items = await ConfigureQuery(query, configureQuery).ToArrayAsync();
            await AfterLoadAsync(items);

            return items;
        }

        public virtual async Task<AddOrUpdateOperationResult<TEntity>> AddAsync(TEntity item,
            IBioRepositoryOperationContext? operationContext = null)
        {
            if (item.Id == Guid.Empty)
            {
                item.Id = Guid.NewGuid();
            }

            (bool isValid, IList<ValidationFailure> errors) validationResult = (false, new List<ValidationFailure>());
            if (await BeforeValidateAsync(item, validationResult, null, operationContext))
            {
                validationResult = await ValidateAsync(item);
                if (validationResult.isValid)
                {
                    if (await BeforeSaveAsync(item, validationResult, null, operationContext))
                    {
                        DbContext.Add(item);
                        await SaveChangesAsync();
                        await AfterSaveAsync(item, null, null, operationContext);
                    }
                }
            }

            return new AddOrUpdateOperationResult<TEntity>(item, validationResult.errors, new PropertyChange[0]);
        }

        public PropertyChange[] GetChanges(TEntity item, TEntity oldEntity)
        {
            var changes = new List<PropertyChange>();
            foreach (var propertyEntry in DbContext.Entry(item).Properties)
            {
                if (propertyEntry.IsModified)
                {
                    var name = propertyEntry.Metadata.Name;
                    var originalValue = propertyEntry.OriginalValue;
                    var value = propertyEntry.CurrentValue;
                    changes.Add(new PropertyChange(name, originalValue, value));
                }
            }

            foreach (var navigationEntry in DbContext.Entry(item).Navigations)
            {
                var property = item.GetType().GetProperty(navigationEntry.Metadata.Name);
                if (property != null)
                {
                    var value = property.GetValue(item);
                    var originalValue = property.GetValue(oldEntity);
                    if (value == null && originalValue != null || value != null && !value.Equals(originalValue))
                    {
                        var name = navigationEntry.Metadata.Name;
                        changes.Add(new PropertyChange(name, originalValue, value));
                    }
                }
            }

            return changes.ToArray();
        }

        public virtual async Task<AddOrUpdateOperationResult<TEntity>> UpdateAsync(TEntity item,
            IBioRepositoryOperationContext? operationContext = null)
        {
            var oldItem = GetBaseQuery().Where(e => e.Id == item.Id).AsNoTracking().First();
            var changes = GetChanges(item, oldItem);
            item.DateUpdated = DateTimeOffset.UtcNow;
            (bool isValid, IList<ValidationFailure> errors) validationResult = (false, new List<ValidationFailure>());
            if (await BeforeValidateAsync(item, validationResult, changes, operationContext))
            {
                validationResult = await ValidateAsync(item, changes);
                if (validationResult.isValid)
                {
                    if (await BeforeSaveAsync(item, validationResult, changes, operationContext))
                    {
                        DbContext.Update(item);
                        await SaveChangesAsync();
                        await AfterSaveAsync(item, changes, oldItem, operationContext);
                    }
                }
            }

            return new AddOrUpdateOperationResult<TEntity>(item, validationResult.errors, changes);
        }

        public Task FinishBatchAsync()
        {
            _batchMode = false;
            return SaveChangesAsync();
        }


        public virtual async Task<TEntity> DeleteAsync(Guid id, IBioRepositoryOperationContext? operationContext = null)
        {
            var item = await GetByIdAsync(id);
            if (item != null)
            {
                DbContext.Remove(item);
                await SaveChangesAsync();
                return item;
            }

            throw new ArgumentException();
        }

        public async Task<TEntity> DeleteAsync(TEntity item, IBioRepositoryOperationContext? operationContext = null)
        {
            DbContext.Attach(item);
            DbContext.Remove(item);
            await SaveChangesAsync();
            return item;
        }

        protected virtual async Task<bool> SaveChangesAsync()
        {
            if (!_batchMode)
            {
                await DbContext.SaveChangesAsync();
                return true;
            }

            return false;
        }

        private bool _batchMode;

        public void BeginBatch()
        {
            _batchMode = true;
        }

        protected virtual async Task<(bool isValid, IList<ValidationFailure> errors)> ValidateAsync(TEntity entity,
            PropertyChange[]? changes = null)
        {
            var failures = new List<ValidationFailure>();
            if (Validators != null)
            {
                foreach (var validator in Validators)
                {
                    var result = await validator.ValidateAsync(entity);
                    if (!result.IsValid)
                    {
                        failures.AddRange(result.Errors);
                    }
                }
            }

            return (!failures.Any(), failures);
        }

        protected virtual IQueryable<TEntity> GetBaseQuery()
        {
            return DbContext.Set<TEntity>().AsQueryable();
        }

        protected virtual IQueryable<TEntity> ConfigureQuery(IQueryable<TEntity> query,
            Func<IQueryable<TEntity>, IQueryable<TEntity>>? configureQuery = null)
        {
            if (configureQuery != null)
            {
                query = configureQuery.Invoke(query);
            }

            return query;
        }


        protected virtual Task<bool> BeforeValidateAsync(TEntity item,
            (bool isValid, IList<ValidationFailure> errors) validationResult,
            PropertyChange[]? changes = null, IBioRepositoryOperationContext? operationContext = null)
        {
            return HooksManager.BeforeValidateAsync(item, validationResult, changes, operationContext);
        }

        protected virtual Task<bool> BeforeSaveAsync(TEntity item,
            (bool isValid, IList<ValidationFailure> errors) validationResult,
            PropertyChange[]? changes = null, IBioRepositoryOperationContext? operationContext = null)
        {
            return HooksManager.BeforeSaveAsync(item, validationResult, changes, operationContext);
        }

        protected virtual async Task<bool> AfterSaveAsync(TEntity item, PropertyChange[]? changes = null,
            TEntity? oldItem = null,
            IBioRepositoryOperationContext? operationContext = null)
        {
            var result = await HooksManager.AfterSaveAsync(item, changes, operationContext);


            if (item.Properties != null)
            {
                foreach (var propertiesEntry in item.Properties)
                {
                    foreach (var val in propertiesEntry.Properties)
                    {
                        await PropertiesProvider.SetAsync(val.Value, item, val.SiteId);
                    }
                }
            }

            return result;
        }
    }
}
