using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using CodeHub.Core.Filters;
using CodeHub.Core.Services;
using CodeHub.Core.ViewModels.Issues;
using CodeHub.Core.ViewModels.PullRequests;
using GitHubSharp.Models;
using CodeHub.Core.ViewModels.Source;
using CodeHub.Core.ViewModels.Changesets;
using ReactiveUI;

using Xamarin.Utilities.Core.ViewModels;
using CodeHub.Core.Utilities;

namespace CodeHub.Core.ViewModels.Notifications
{
    public class NotificationsViewModel : BaseViewModel, ILoadableViewModel
    {
        private readonly ReactiveList<NotificationModel> _notifications = new ReactiveList<NotificationModel>();
        private readonly IApplicationService _applicationService;
        private int _shownIndex;
        private NotificationsFilterModel _filter;

        public IReadOnlyReactiveList<NotificationModel> Notifications { get; private set; }

		public int ShownIndex
		{
			get { return _shownIndex; }
			set { this.RaiseAndSetIfChanged(ref _shownIndex, value); }
		}

        public NotificationsFilterModel Filter
        {
            get { return _filter; }
            private set { this.RaiseAndSetIfChanged(ref _filter, value); }
        }

        public IReactiveCommand LoadCommand { get; private set; }

        public IReactiveCommand ReadRepositoriesCommand { get; private set; }

        public IReactiveCommand ReadAllCommand { get; private set; }

        public IReactiveCommand<object> GoToNotificationCommand { get; private set; }

        private string _searchKeyword;
        public string SearchKeyword
        {
            get { return _searchKeyword; }
            set { this.RaiseAndSetIfChanged(ref _searchKeyword, value); }
        }
		
        private void GoToNotification(NotificationModel x)
        {
            var subject = x.Subject.Type.ToLower();
            if (subject.Equals("issue"))
            {
                var node = x.Subject.Url.Substring(x.Subject.Url.LastIndexOf('/') + 1);
                var vm = CreateViewModel<IssueViewModel>();
                vm.RepositoryOwner = x.Repository.Owner.Login;
                vm.RepositoryName = x.Repository.Name;
                vm.IssueId = long.Parse(node);
                ShowViewModel(vm);
            }
            else if (subject.Equals("pullrequest"))
            {
                var node = x.Subject.Url.Substring(x.Subject.Url.LastIndexOf('/') + 1);
                var vm = CreateViewModel<PullRequestViewModel>();
                vm.RepositoryOwner = x.Repository.Owner.Login;
                vm.RepositoryName = x.Repository.Name;
                vm.PullRequestId = long.Parse(node);
                ShowViewModel(vm);
            }
            else if (subject.Equals("commit"))
            {
                var node = x.Subject.Url.Substring(x.Subject.Url.LastIndexOf('/') + 1);
                var vm = CreateViewModel<ChangesetViewModel>();
                vm.RepositoryOwner = x.Repository.Owner.Login;
                vm.RepositoryName = x.Repository.Name;
                vm.Node = node;
                ShowViewModel(vm);
            }
            else if (subject.Equals("release"))
            {
                var vm = CreateViewModel<BranchesAndTagsViewModel>();
                vm.RepositoryOwner = x.Repository.Owner.Login;
                vm.RepositoryName = x.Repository.Name;
                vm.SelectedFilter = BranchesAndTagsViewModel.ShowIndex.Tags;
                ShowViewModel(vm);
            }

            ReadNotification(x);
        }

        public NotificationsViewModel(IApplicationService applicationService)
        {
            _applicationService = applicationService;
            Notifications = _notifications.CreateDerivedCollection(x => x, 
                x => x.Subject.Title.ContainsKeyword(SearchKeyword), 
                signalReset: this.WhenAnyValue(x => x.SearchKeyword));

            LoadCommand = ReactiveCommand.CreateAsyncTask(t =>
            {
                var req = applicationService.Client.Notifications.GetAll(all: Filter.All, participating: Filter.Participating);
                return this.RequestModel(req, t as bool?, response => _notifications.Reset(response.Data));
            });

            GoToNotificationCommand = ReactiveCommand.Create();
            GoToNotificationCommand.OfType<NotificationModel>().Subscribe(GoToNotification);


            var canReadAll = Notifications.CountChanged.Select(x => x > 0).CombineLatest(
                this.WhenAnyValue(x => x.ShownIndex).Select(x => x != 2), (x, y) => x & y);

            ReadAllCommand = ReactiveCommand.CreateAsyncTask(canReadAll, async t =>
                {
                    try
                    {
                        if (!Notifications.Any())
                            return;
                        await applicationService.Client.ExecuteAsync(applicationService.Client.Notifications.MarkAsRead());
                        _notifications.Clear();
                    }
                    catch (Exception e)
                    {
                        throw new Exception("Unable to mark all notifications as read. Please try again.", e);
                    }
                });

            ReadRepositoriesCommand = ReactiveCommand.CreateAsyncTask(async t =>
            {
                try
                {
                    var repo = t as string;
                    if (repo == null) return;
                    var repoId = new RepositoryIdentifier(repo);
                    await applicationService.Client.ExecuteAsync(applicationService.Client.Notifications.MarkRepoAsRead(repoId.Owner, repoId.Name));
                    _notifications.RemoveAll(Notifications.Where(x => string.Equals(x.Repository.FullName, repo, StringComparison.OrdinalIgnoreCase)).ToList());
                }
                catch (Exception e)
                {
                    throw new Exception("Unable to mark repositories' notifications as read. Please try again.", e);
                }
            });

            this.WhenAnyValue(x => x.ShownIndex).Subscribe(x =>
            {
                switch (x)
                {
                    case 0:
                        Filter = NotificationsFilterModel.CreateUnreadFilter();
                        break;
                    case 1:
                        Filter = NotificationsFilterModel.CreateParticipatingFilter();
                        break;
                    default:
                        Filter = NotificationsFilterModel.CreateAllFilter();
                        break;
                }
            });

            this.WhenAnyValue(x => x.Filter).Skip(1).Subscribe(x => LoadCommand.ExecuteIfCan());
        }

        private async Task ReadNotification(NotificationModel notification)
        {
            try
            {
                if (notification == null) return;
                if (!notification.Unread) return;
                var response = await _applicationService.Client.ExecuteAsync(_applicationService.Client.Notifications[notification.Id].MarkAsRead());
                if (response.Data)
                {
                    notification.Unread = false;
                    _notifications.Remove(notification);
                }
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Unable to mark notification as read: " + e.Message);
            }
        }
    }
}

