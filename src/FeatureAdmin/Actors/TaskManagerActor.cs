﻿using Akka.Actor;
using Akka.DI.Core;
using Akka.Event;
using Caliburn.Micro;
using FeatureAdmin.Core.Messages;
using FeatureAdmin.Core.Messages.Tasks;
using FeatureAdmin.Core.Models;
using FeatureAdmin.Core.Models.Tasks;
using FeatureAdmin.Messages;
using System;
using System.Linq;
using System.Collections.Generic;
using FeatureAdmin.Core;

namespace FeatureAdmin.Actors
{
    public class TaskManagerActor : ReceiveActor
    {
        private readonly IEventAggregator eventAggregator;
        private readonly IActorRef featureDefinitionActor;
        private readonly Dictionary<Guid, IActorRef> locationActors;
        private readonly ILoggingAdapter _log = Logging.GetLogger(Context);
        private readonly Dictionary<Guid, AdminTaskItems> tasks;

        public TaskManagerActor(IEventAggregator eventAggregator)
        {
            this.eventAggregator = eventAggregator;

            locationActors = new Dictionary<Guid, IActorRef>();

            tasks = new Dictionary<Guid, AdminTaskItems>();

            featureDefinitionActor =
                    Context.ActorOf(Context.DI().Props<FeatureDefinitionActor>());


            Receive<NewTask>(message => HandleNewTask(message));
            Receive<ClearItemsReady>(message => HandleClearItems(message));

            Receive<ItemUpdated<IEnumerable<Location>>>(message => LocationUpdated(message));
            Receive<ItemUpdated<IEnumerable<FeatureDefinition>>>(message => FeatureDefinitionsUpdated(message));
        }

        private void HandleNewTask(NewTask message)
        {
            // log new task started
            SendProgress(message.Task);

            // add new task to tracking list
            tasks.Add(message.Task.Id, message.Task);

            // delegate tasks and todos depending on task type
            switch (message.TaskType)
            {
                case Core.Models.Enums.TaskType.Load:
                    InitiateLoadTask(message.Task);
                    break;
                //case Core.Models.Enums.TaskType.Act:
                //    break;
                //case Core.Models.Enums.TaskType.Update:
                //    break;
                //case Core.Models.Enums.TaskType.Uninstall:
                //    break;
                default:
                    var logUnknownTaskType = new Messages.LogMessage(Core.Models.Enums.LogLevel.Error,
                string.Format("Canceling due to unknown task type, task '{1}' (ID: '{0}')", message.Task.Id, message.Task.Title)
                );
                    eventAggregator.BeginPublishOnUIThread(logUnknownTaskType);
                    tasks.Remove(message.Task.Id);
                    break;
            }
        }
        private void InitiateLoadTask(AdminTaskItems task)
        {
            // clean all feature definition and location collections
            var clearMessage = new ClearItems(task.Id);
            eventAggregator.PublishOnUIThread(clearMessage);
        }


        private void LoadTask([NotNull] LoadLocationQuery message)
        {
            _log.Debug("Entered TaskManager-LoadTask");

            var locationId = message.Location.Id;

            if (!locationActors.ContainsKey(locationId))
            {
                IActorRef newLocationActor =
                    Context.ActorOf(Context.DI().Props<LocationActor>());

                locationActors.Add(locationId, newLocationActor);

                newLocationActor.Tell(message);
            }
            else
            {
                locationActors[locationId].Tell(message);
            }
        }

        public void HandleClearItems(ClearItemsReady message)
        {
            var task = tasks[message.TaskId];

            // how many steps are expected is decided in Common.Constants.Tasks.PreparationStepsForLoad
            var preparationReady = task.TrackPreparationsProcessed(1);

            if (preparationReady)
            {
                SendProgress(task);

                featureDefinitionActor.Tell(message);

                var loadLocation = Core.Factories.LocationFactory.GetDummyFarmForLoadCommand();
                var locationQuery = new LoadLocationQuery(message.TaskId, loadLocation);
                LoadTask(locationQuery);
            }

        }

        private void SendProgress(AdminTaskItems task)
        {
            var progressMsg = new ProgressMessage(task);
            eventAggregator.PublishOnUIThread(progressMsg);

            if (task.PercentCompleted == 0 && task.Start == null)
            {
                task.Start = DateTime.Now;
                var logMsg = new Messages.LogMessage(Core.Models.Enums.LogLevel.Information,
                string.Format("Started '{1}' (ID: '{0}')", task.Id, task.Title)
                );
                eventAggregator.PublishOnUIThread(logMsg);
            }

            if (task.PercentCompleted >= 1 && task.End == null)
            {
                task.End = DateTime.Now;
                var logMsg = new Messages.LogMessage(Core.Models.Enums.LogLevel.Information,
                string.Format("Completed '{1}' (ID: '{0}')", task.Id, task.Title)
                );
                eventAggregator.PublishOnUIThread(logMsg);

                // as task list ist deleted after restart, no need to delete tasks here
            }

        }

        private void FeatureDefinitionsUpdated(ItemUpdated<IEnumerable<FeatureDefinition>> message)
        {
            eventAggregator.PublishOnUIThread(message);

            var task = tasks[message.TaskId];

            var stepReady = task.TrackFeatureDefinitionsProcessed(message.Item.Count());

            if (stepReady)
            {
                SendProgress(task);
            }
        }

        private void LocationUpdated([NotNull] ItemUpdated<IEnumerable<Location>> message)
        {
            var locations = message.Item;

            // publish locations to wpf
            eventAggregator.PublishOnUIThread(message);

            var task = tasks[message.TaskId];


            var stepReady = task.TrackLocationsProcessed(message.Item);

            SendProgress(task);

            // for web applications, load children


            // convert features for feature definitions

            var features = locations.SelectMany(l => l.ActivatedFeatures);

            var featureDefinitions = features.Select(f => f.Definition).Distinct().ToList();

            foreach (FeatureDefinition fd in featureDefinitions)
            {
                foreach (var feature in features.Where(f => f.Definition == fd))
                {
                    fd.ToggleActivatedFeature(feature, true);
                }
            }

            eventAggregator.PublishOnUIThread(new ItemUpdated<IEnumerable<FeatureDefinition>>(
                 message.TaskId,
                 featureDefinitions));
        }
    }
}
