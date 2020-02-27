﻿#region Reference

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Revit.Async.Entities;
using Revit.Async.ExternalEvents;
using Revit.Async.Interfaces;
using Revit.Async.Utils;

#if NET40
using Revit.Async.Extensions;

#endif

#endregion

namespace Revit.Async
{
    /// <summary>
    ///     Provide some useful methods to support running Revit API code from any context
    /// </summary>
    public class RevitTask
    {
        #region Fields

        private static ConcurrentDictionary<Type, ExternalEventPair> _asyncDelegateExternalEvents;
        private static ConcurrentDictionary<Type, ExternalEventPair> _syncDelegateExternalEvents;
        private static ConcurrentDictionary<Type, ExternalEventPair> _registeredExternalEvents;

        #endregion

        #region Properties

        /// <summary>
        ///     Cache all the created <see cref="ExternalEvent" />s and their <see cref="IExternalEventHandler" /> running async code
        /// </summary>
        private static ConcurrentDictionary<Type, ExternalEventPair> AsyncDelegateExternalEvents =>
            _asyncDelegateExternalEvents ?? (_asyncDelegateExternalEvents = new ConcurrentDictionary<Type, ExternalEventPair>());

        /// <summary>
        ///     Cache all the created <see cref="ExternalEvent" />s and their <see cref="IExternalEventHandler" /> running sync code
        /// </summary>
        private static ConcurrentDictionary<Type, ExternalEventPair> SyncDelegateExternalEvents =>
            _syncDelegateExternalEvents ?? (_syncDelegateExternalEvents = new ConcurrentDictionary<Type, ExternalEventPair>());

        /// <summary>
        ///     Use to create any other external events
        /// </summary>
        private static ExternalEventPair ExternalEventCreator { get; set; }

        private static ConcurrentDictionary<Type, ExternalEventPair> RegisteredExternalEvents =>
            _registeredExternalEvents ?? (_registeredExternalEvents = new ConcurrentDictionary<Type, ExternalEventPair>());

        #endregion

        #region Others

        /// <summary>
        ///     Always call this method ahead of time in Revit API context to make sure that <see cref="RevitTask" /> functions
        ///     properly
        /// </summary>
        public static void Initialize()
        {
            if (ExternalEventCreator == null)
            {
                ExternalEventCreator = new ExternalEventPair(new SyncDelegateExternalEventHandler<ExternalEvent>());
            }
        }

        /// <summary>
        ///     Raise an <see cref="IGenericExternalEventHandler{TParameter,TResult}" /> and get the result
        /// </summary>
        /// <typeparam name="THandler">The type of the <see cref="IGenericExternalEventHandler{TParameter,TResult}" /></typeparam>
        /// <typeparam name="TParameter">
        ///     The type of the parameter that
        ///     <see cref="IGenericExternalEventHandler{TParameter,TResult}" /> accepts
        /// </typeparam>
        /// <typeparam name="TResult">
        ///     The type of result that <see cref="IGenericExternalEventHandler{TParameter,TResult}" />
        ///     generates
        /// </typeparam>
        /// <param name="parameter">
        ///     The parameter passed to the <see cref="IGenericExternalEventHandler{TParameter,TResult}" />
        /// </param>
        /// <returns>The result generated by <see cref="IGenericExternalEventHandler{TParameter,TResult}" /></returns>
        public static Task<TResult> Raise<THandler, TParameter, TResult>(TParameter parameter)
            where THandler : IGenericExternalEventHandler<TParameter, TResult>
        {
            if (RegisteredExternalEvents.TryGetValue(typeof(THandler), out var pair))
            {
                return RunAsync<TParameter, TResult>(pair, parameter);
            }

            return TaskUtils.FromResult(default(TResult));
        }

        /// <summary>
        ///     Register an <see cref="IGenericExternalEventHandler{TParameter,TResult}" /> to <see cref="RevitTask" /> to be
        ///     raised
        /// </summary>
        /// <typeparam name="TParameter">
        ///     The type of the parameter that
        ///     <see cref="IGenericExternalEventHandler{TParameter,TResult}" /> accepts
        /// </typeparam>
        /// <typeparam name="TResult">
        ///     The type of result that <see cref="IGenericExternalEventHandler{TParameter,TResult}" />
        ///     generates
        /// </typeparam>
        /// <param name="handler">The instance of <see cref="IGenericExternalEventHandler{TParameter,TResult}" /></param>
        public static void Register<TParameter, TResult>(IGenericExternalEventHandler<TParameter, TResult> handler)
        {
            RegisteredExternalEvents.TryAdd(handler.GetType(), new ExternalEventPair(handler));
        }

        /// <summary>
        ///     Running Revit API code and get the result asynchronously
        /// </summary>
        /// <typeparam name="TResult">The type of the Result</typeparam>
        /// <param name="function">The delegate method wraps all the Revit API code with no argument</param>
        /// <returns></returns>
        public static Task<TResult> RunAsync<TResult>(Func<TResult> function)
        {
            return RunAsync(app => function());
        }

        /// <summary>
        ///     Running Revit API code and get the result asynchronously
        /// </summary>
        /// <typeparam name="TResult">The type of the Result</typeparam>
        /// <param name="function">The delegate method wraps all the Revit API code with <see cref="UIApplication" /> as argument</param>
        /// <returns>The result</returns>
        public static Task<TResult> RunAsync<TResult>(Func<UIApplication, TResult> function)
        {
            var externalEventPair = SyncDelegateExternalEvents.GetOrAdd(typeof(TResult), _ =>
            {
                var handler = new SyncDelegateExternalEventHandler<TResult>();
                return new ExternalEventPair(handler, () => CreateExternalEvent(handler));
            });
            return RunAsync<Func<UIApplication, TResult>, TResult>(externalEventPair, function);
        }

        /// <summary>
        ///     Running Revit API code and get the result asynchronously
        /// </summary>
        /// <typeparam name="TResult">The type of the Result</typeparam>
        /// <param name="function">
        ///     The delegate method wraps all the Revit API code and some other asynchronous processes with no
        ///     argument
        /// </param>
        /// <returns>The result</returns>
        public static Task<TResult> RunAsync<TResult>(Func<Task<TResult>> function)
        {
            return RunAsync(_ => function());
        }

        /// <summary>
        ///     Running Revit API code and get the result asynchronously
        /// </summary>
        /// <typeparam name="TResult">The type of the Result</typeparam>
        /// <param name="function">
        ///     The delegate method wraps all the Revit API code and some other asynchronous processes with
        ///     <see cref="UIApplication" /> as argument
        /// </param>
        /// <returns></returns>
        public static Task<TResult> RunAsync<TResult>(Func<UIApplication, Task<TResult>> function)
        {
            var externalEventPair = AsyncDelegateExternalEvents.GetOrAdd(typeof(TResult), _ =>
            {
                var handler = new AsyncDelegateExternalEventHandler<TResult>();
                return new ExternalEventPair(handler, () => CreateExternalEvent(handler));
            });
            return RunAsync<Func<UIApplication, Task<TResult>>, TResult>(externalEventPair, function);
        }

        /// <summary>
        ///     Running Revit API code asynchronously
        /// </summary>
        /// <param name="action">The delegate method wraps all the Revit API code</param>
        /// <returns>The task indicating whether the execution has completed</returns>
        public static Task RunAsync(Action action)
        {
            return RunAsync(_ => action());
        }

        /// <summary>
        ///     Running Revit API code asynchronously
        /// </summary>
        /// <param name="action">The delegate method wraps all the Revit API code with <see cref="UIApplication" /> as argument</param>
        /// <returns>The task indicating whether the execution has completed</returns>
        public static Task RunAsync(Action<UIApplication> action)
        {
            return RunAsync(app =>
            {
                action(app);
                return (object) null;
            });
        }

        /// <summary>
        ///     Running Revit API code asynchronously
        /// </summary>
        /// <param name="function">The delegate method wraps all the Revit API code and some other asynchronous processes</param>
        /// <returns>The task indicating whether the execution has completed</returns>
        public static Task RunAsync(Func<Task> function)
        {
            return RunAsync(_ => function());
        }

        /// <summary>
        ///     Running Revit API code asynchronously
        /// </summary>
        /// <param name="function">
        ///     The delegate method wraps all the Revit API code and some other asynchronous processes with
        ///     <see cref="UIApplication" /> as argument
        /// </param>
        /// <returns></returns>
        public static Task RunAsync(Func<UIApplication, Task> function)
        {
            return RunAsync(app => function(app).ContinueWith(task => (object) null));
        }

        /// <summary>
        ///     Create a new <see cref="ExternalEvent" /> for an <see cref="IExternalEventHandler" /> instance
        /// </summary>
        /// <param name="handler">The <see cref="IExternalEventHandler" /> instance</param>
        /// <returns>The <see cref="ExternalEvent" /> created</returns>
        private static Task<ExternalEvent> CreateExternalEvent(IExternalEventHandler handler)
        {
            return RunAsync<Func<UIApplication, ExternalEvent>, ExternalEvent>(ExternalEventCreator, app => ExternalEvent.Create(handler));
        }

#if NET40
        private static Task<TResult> RunAsync<TParameter, TResult>(ExternalEventPair externalEventPair, TParameter parameter)
#else
        private static async Task<TResult> RunAsync<TParameter, TResult>(ExternalEventPair externalEventPair, TParameter parameter)
#endif
        {
            var genericHandler = (IGenericExternalEventHandler<TParameter, TResult>) externalEventPair.Handler;
            var task           = genericHandler.Prepare(parameter);
#if NET40
            return new TaskCompletionSource<TResult>()
                  .Await(externalEventPair.GetTrigger(), (trigger, tcs) =>
                   {
                       trigger.Raise();
                       tcs.Await(task);
                   }).Task;
#else
            var trigger = await externalEventPair.GetTrigger();
            trigger.Raise();
            return await task;
#endif
        }

        #endregion
    }
}