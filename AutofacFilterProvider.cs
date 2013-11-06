// This software is part of the Autofac IoC container
// Copyright � 2012 Autofac Contributors
// http://autofac.org
//
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Web.Mvc;
using System.Web.Mvc.Async;
using System.Web.Mvc.Filters;
using Autofac.Features.Metadata;

namespace Autofac.Integration.Mvc
{
    /// <summary>
    /// Defines a filter provider for filter attributes that performs property injection.
    /// </summary>
    [SecurityCritical]
    public class AutofacFilterProvider : FilterAttributeFilterProvider
    {
        class FilterContext
        {
            public ActionDescriptor ActionDescriptor { get; set; }
            public ILifetimeScope LifetimeScope { get; set; }
            public Type ControllerType { get; set; }
            public List<Filter> Filters { get; set; }
        }

        internal static string ActionFilterMetadataKey = "AutofacMvcActionFilter";
        internal static string OverrideActionFilterMetadataKey = "AutofacMvcOverrideActionFilter";

        internal static string AuthorizationFilterMetadataKey = "AutofacMvcAuthorizationFilter";
        internal static string OverrideAuthorizationFilterMetadataKey = "AutofacMvcOverrideAuthorizationFilter";

        internal static string AuthenticationFilterMetadataKey = "AutofacMvcAuthenticationFilter";
        internal static string OverrideAuthenticationFilterMetadataKey = "AutofacMvcOverrideAuthenticationFilter";

        internal static string ExceptionFilterMetadataKey = "AutofacMvcExceptionFilter";
        internal static string OverrideExceptionFilterMetadataKey = "AutofacMvcOverrideExceptionFilter";

        internal static string ResultFilterMetadataKey = "AutofacMvcResultFilter";
        internal static string OverrideResultFilterMetadataKey = "AutofacMvcOverrideResultFilter";

        /// <summary>
        /// Initializes a new instance of the <see cref="AutofacFilterProvider"/> class.
        /// </summary>
        /// <remarks>
        /// The <c>false</c> constructor parameter passed to base here ensures that attribute instances are not cached.
        /// </remarks>
        public AutofacFilterProvider() : base(false)
        {
        }

        /// <summary>
        /// Aggregates the filters from all of the filter providers into one collection.
        /// </summary>
        /// <param name="controllerContext">The controller context.</param>
        /// <param name="actionDescriptor">The action descriptor.</param>
        /// <returns>
        /// The collection filters from all of the filter providers with properties injected.
        /// </returns>
        /// <exception cref="System.ArgumentNullException">
        /// Thrown if <paramref name="controllerContext" /> is <see langword="null" />.
        /// </exception>
        [SecurityCritical]
        public override IEnumerable<Filter> GetFilters(ControllerContext controllerContext, ActionDescriptor actionDescriptor)
        {
            if (controllerContext == null)
            {
                throw new ArgumentNullException("controllerContext");
            }
            var filters = base.GetFilters(controllerContext, actionDescriptor).ToList();
            var lifetimeScope = AutofacDependencyResolver.Current.RequestLifetimeScope;

            if (lifetimeScope != null)
            {
                foreach (var filter in filters)
                    lifetimeScope.InjectProperties(filter.Instance);

                var controllerType = controllerContext.Controller.GetType();

                var filterContext = new FilterContext
                {
                    ActionDescriptor = actionDescriptor,
                    LifetimeScope = lifetimeScope,
                    ControllerType = controllerType,
                    Filters = filters
                };

                ResolveControllerScopedFilters(filterContext);

                ResolveActionScopedFilters<ReflectedActionDescriptor>(filterContext, d => d.MethodInfo);
                ResolveActionScopedFilters<ReflectedAsyncActionDescriptor>(filterContext, d => d.AsyncMethodInfo);
                ResolveActionScopedFilters<TaskAsyncActionDescriptor>(filterContext, d => d.TaskMethodInfo);

                ResolveControllerScopedOverrideFilters(filterContext);

                ResolveActionScopedOverrideFilters<ReflectedActionDescriptor>(filterContext, d => d.MethodInfo);
                ResolveActionScopedOverrideFilters<ReflectedAsyncActionDescriptor>(filterContext, d => d.AsyncMethodInfo);
                ResolveActionScopedOverrideFilters<TaskAsyncActionDescriptor>(filterContext, d => d.TaskMethodInfo);
            }

            return filters.ToArray();
        }

        static void ResolveControllerScopedFilters(FilterContext filterContext)
        {
            ResolveControllerScopedFilter<IActionFilter>(filterContext, ActionFilterMetadataKey);
            ResolveControllerScopedFilter<IAuthenticationFilter>(filterContext, AuthenticationFilterMetadataKey);
            ResolveControllerScopedFilter<IAuthorizationFilter>(filterContext, AuthorizationFilterMetadataKey);
            ResolveControllerScopedFilter<IExceptionFilter>(filterContext, ExceptionFilterMetadataKey);
            ResolveControllerScopedFilter<IResultFilter>(filterContext, ResultFilterMetadataKey);
        }

        static void ResolveControllerScopedFilter<TFilter>(FilterContext filterContext, string metadataKey)
            where TFilter : class
        {
            var actionFilters = filterContext.LifetimeScope.Resolve<IEnumerable<Meta<Lazy<TFilter>>>>();

            foreach (var actionFilter in actionFilters.Where(a => a.Metadata.ContainsKey(metadataKey) && a.Metadata[metadataKey] is FilterMetadata))
            {
                var metadata = (FilterMetadata)actionFilter.Metadata[metadataKey];
                if (!FilterMatchesController(filterContext, metadata)) continue;

                var filter = new Filter(actionFilter.Value.Value, FilterScope.Controller, metadata.Order);
                filterContext.Filters.Add(filter);
            }
        }

        static void ResolveActionScopedFilters<T>(FilterContext filterContext, Func<T, MethodInfo> methodSelector)
            where T : ActionDescriptor
        {
            var actionDescriptor = filterContext.ActionDescriptor as T;
            if (actionDescriptor == null) return;

            var methodInfo = methodSelector(actionDescriptor);

            ResolveActionScopedFilter<IActionFilter>(filterContext, methodInfo, ActionFilterMetadataKey);
            ResolveActionScopedFilter<IAuthenticationFilter>(filterContext, methodInfo, AuthenticationFilterMetadataKey);
            ResolveActionScopedFilter<IAuthorizationFilter>(filterContext, methodInfo, AuthorizationFilterMetadataKey);
            ResolveActionScopedFilter<IExceptionFilter>(filterContext, methodInfo, ExceptionFilterMetadataKey);
            ResolveActionScopedFilter<IResultFilter>(filterContext, methodInfo, ResultFilterMetadataKey);
        }

        static void ResolveActionScopedFilter<TFilter>(FilterContext filterContext, MethodInfo methodInfo, string metadataKey)
            where TFilter : class
        {
            var actionFilters = filterContext.LifetimeScope.Resolve<IEnumerable<Meta<Lazy<TFilter>>>>();

            foreach (var actionFilter in actionFilters.Where(a => a.Metadata.ContainsKey(metadataKey) && a.Metadata[metadataKey] is FilterMetadata))
            {
                var metadata = (FilterMetadata)actionFilter.Metadata[metadataKey];
                if (!FilterMatchesAction(filterContext, methodInfo, metadata)) continue;

                var filter = new Filter(actionFilter.Value.Value, FilterScope.Action, metadata.Order);
                filterContext.Filters.Add(filter);
            }
        }

        static void ResolveControllerScopedOverrideFilters(FilterContext filterContext)
        {
            ResolveControllerScopedOverrideFilter(filterContext, OverrideActionFilterMetadataKey);
            ResolveControllerScopedOverrideFilter(filterContext, OverrideAuthenticationFilterMetadataKey);
            ResolveControllerScopedOverrideFilter(filterContext, OverrideAuthorizationFilterMetadataKey);
            ResolveControllerScopedOverrideFilter(filterContext, OverrideExceptionFilterMetadataKey);
            ResolveControllerScopedOverrideFilter(filterContext, OverrideResultFilterMetadataKey);
        }

        static void ResolveControllerScopedOverrideFilter(FilterContext filterContext, string metadataKey)
        {
            var actionFilters = filterContext.LifetimeScope.Resolve<IEnumerable<Meta<IOverrideFilter>>>();

            foreach (var actionFilter in actionFilters.Where(a => a.Metadata.ContainsKey(metadataKey) && a.Metadata[metadataKey] is FilterMetadata))
            {
                var metadata = (FilterMetadata)actionFilter.Metadata[metadataKey];
                if (!FilterMatchesController(filterContext, metadata)) continue;

                var filter = new Filter(actionFilter.Value, FilterScope.Controller, metadata.Order);
                filterContext.Filters.Add(filter);
            }
        }

        static void ResolveActionScopedOverrideFilters<T>(FilterContext filterContext, Func<T, MethodInfo> methodSelector)
            where T : ActionDescriptor
        {
            var actionDescriptor = filterContext.ActionDescriptor as T;
            if (actionDescriptor == null) return;

            var methodInfo = methodSelector(actionDescriptor);

            ResolveActionScopedOverrideFilter(filterContext, methodInfo, OverrideActionFilterMetadataKey);
            ResolveActionScopedOverrideFilter(filterContext, methodInfo, OverrideAuthenticationFilterMetadataKey);
            ResolveActionScopedOverrideFilter(filterContext, methodInfo, OverrideAuthorizationFilterMetadataKey);
            ResolveActionScopedOverrideFilter(filterContext, methodInfo, OverrideExceptionFilterMetadataKey);
            ResolveActionScopedOverrideFilter(filterContext, methodInfo, OverrideResultFilterMetadataKey);
        }

        static void ResolveActionScopedOverrideFilter(FilterContext filterContext, MethodInfo methodInfo, string metadataKey)
        {
            var actionFilters = filterContext.LifetimeScope.Resolve<IEnumerable<Meta<IOverrideFilter>>>();

            foreach (var actionFilter in actionFilters.Where(a => a.Metadata.ContainsKey(metadataKey) && a.Metadata[metadataKey] is FilterMetadata))
            {
                var metadata = (FilterMetadata)actionFilter.Metadata[metadataKey];
                if (!FilterMatchesAction(filterContext, methodInfo, metadata)) continue;

                var filter = new Filter(actionFilter.Value, FilterScope.Action, metadata.Order);
                filterContext.Filters.Add(filter);
            }
        }

        static bool FilterMatchesController(FilterContext filterContext, FilterMetadata metadata)
        {
            return metadata.ControllerType != null
                   && metadata.ControllerType.IsAssignableFrom(filterContext.ControllerType)
                   && metadata.FilterScope == FilterScope.Controller
                   && metadata.MethodInfo == null;
        }

        static bool FilterMatchesAction(FilterContext filterContext, MethodInfo methodInfo, FilterMetadata metadata)
        {
            return metadata.ControllerType != null
                   && metadata.ControllerType.IsAssignableFrom(filterContext.ControllerType)
                   && metadata.FilterScope == FilterScope.Action
                   && metadata.MethodInfo.GetBaseDefinition() == methodInfo.GetBaseDefinition();
        }
    }
}
