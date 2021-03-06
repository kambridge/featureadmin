﻿using Caliburn.Micro;
using FeatureAdmin.Core.Models;
using FeatureAdmin.Messages;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System;
using FeatureAdmin.Core.Models.Enums;
using FeatureAdmin.Repository;

namespace FeatureAdmin.ViewModels
{
    public abstract class BaseListViewModel<T> : BaseItemViewModel<T>, IHandle<ProgressMessage>, IHandle<SetSearchFilter<T>> where T : class, IBaseItem
    {

        protected DateTime lastUpdateInitiatedSearch;
        protected IFeatureRepository repository;
        protected string searchInput;

        private Scope? selectedScopeFilter;
        public BaseListViewModel(IEventAggregator eventAggregator, IFeatureRepository repository) :
            base(eventAggregator)
        {
            ScopeFilters = new ObservableCollection<Scope>(Common.Constants.Search.ScopeFilterList);
            
            lastUpdateInitiatedSearch = DateTime.Now;

            // https://github.com/Fody/PropertyChanged/issues/269
            ActivationProcessed += (s, e) => SelectionChanged();

            this.repository = repository;
        }

        public bool CanFilterThis { get; protected set; }

        public ObservableCollection<Scope> ScopeFilters { get; private set; }

        public string SearchInput
        {
            get { return searchInput; }
            set
            {
                searchInput = value;
                FilterResults();
            }
        }

        public Scope? SelectedScopeFilter
        {
            get { return selectedScopeFilter; }
            set
            {
                selectedScopeFilter = value;
                FilterResults();
            }
        }

        public void FilterThis()
        {
            var searchFilter = new SetSearchFilter<T>(
                ActiveItem == null ? string.Empty : ActiveItem.Id.ToString(), null);
            Handle(searchFilter);
        }

        public void Handle(SetSearchFilter<T> message)
        {
            if (message == null)
            {
                return;
            };

            if (message.SetQuery)
            {
                SearchInput = message.SearchQuery;
            }

            if (message.SetScope)
            {
                SelectedScopeFilter = message.SearchScope;
            }

        }

        /// <summary>
        /// whenever Progress is made, update the search results
        /// </summary>
        /// <param name="message"></param>
        public void Handle(ProgressMessage message)
        {
            FilterResults();
        }

        protected abstract void FilterResults();

        protected void ShowResults(IEnumerable<T> searchResult)
        {
            var activeItemCache = ActiveItem;

            Items.Clear();
            Items.AddRange(searchResult);

            if (activeItemCache != null)
            {
                if (Items.Contains(activeItemCache))
                {
                    ActivateItem(activeItemCache);
                }
                else
                {
                    SelectionChanged();
                }
            }
        }

        public abstract void SelectionChanged();


        protected virtual void SelectionChangedBase()
        {
            eventAggregator.PublishOnUIThread(
                 new Messages.ItemSelected<T>(ActiveItem)
                 );

            CanShowDetails = ActiveItem != null;
            CanFilterThis = ActiveItem != null;
        }
    }
}
