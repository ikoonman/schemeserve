// -----------------------------------------------------------------------------
// File:     CQCControllerPagingTests.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-15
// Purpose:  Unit tests for the page/perPage query values.
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using SchemeserveDotnetTest.Controllers;

namespace SchemeserveDotnetTest.Tests
{
    // "Soft" tests for the page and perPage query-string values of GET v1/providers.
    //
    // In the running app, ASP.NET reads ?page=..&perPage=.. before GetProviders is called and, because of
    // [ApiController], answers 400 itself when a value can't be read as a number or fails its [Range].
    // These tests run the same two pieces directly against the real GetProviders parameters:
    //   1. ASP.NET's own binder, which turns the query-string text into an int? (or records an error), and
    //   2. the [Range] attributes on the parameters.
    //
    // Not included: an integration test that sends a real HTTP request through the whole app
    // (e.g. with WebApplicationFactory) and checks the final 400 response.
    public class CQCControllerPagingTests
    {
        private static ParameterInfo GetProvidersParameter(string name)
        {
            return typeof(CQCController).GetMethod("GetProviders")!.GetParameters().Single(p => p.Name == name);
        }

        // Runs ASP.NET's binder for one query-string value, the way it happens for a real request,
        // and returns the binding context: its Result holds the number read, its ModelState any error.
        private static ModelBindingContext BindQueryValue(string parameterName, string value)
        {
            var parameter = GetProvidersParameter(parameterName);
            var metadata = new EmptyModelMetadataProvider().GetMetadataForType(parameter.ParameterType);

            var query = new QueryCollection(new Dictionary<string, StringValues> { { parameterName, value } });
            var valueProvider = new QueryStringValueProvider(BindingSource.Query, query, CultureInfo.InvariantCulture);
            var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ControllerActionDescriptor());

            var bindingContext = DefaultModelBindingContext.CreateBindingContext(actionContext, valueProvider, metadata, null, parameterName);
            var binder = new SimpleTypeModelBinder(parameter.ParameterType, NullLoggerFactory.Instance);
            binder.BindModelAsync(bindingContext).GetAwaiter().GetResult();

            return bindingContext;
        }

        // Tests: text that isn't a whole number (e.g. ?page=4&perPage=AJJJ) can't be read into page or perPage,
        // so ASP.NET records an error; in the running app that becomes a 400 before the controller is called.
        [Theory]
        [InlineData("page", "AJJJ")]
        [InlineData("perPage", "AJJJ")]
        [InlineData("page", "1.5")]
        [InlineData("perPage", "ten")]
        public void PagingValue_NotAWholeNumber_IsRejected(string parameterName, string value)
        {
            // Act
            var binding = BindQueryValue(parameterName, value);

            // Assert
            Assert.False(binding.Result.IsModelSet);
            Assert.Equal(1, binding.ModelState.ErrorCount);
            Assert.NotEmpty(binding.ModelState[parameterName]!.Errors);
        }

        // Tests: whole numbers are read without errors (the opposite case of the test above).
        [Theory]
        [InlineData("page", "4", 4)]
        [InlineData("perPage", "10", 10)]
        public void PagingValue_WholeNumber_IsAccepted(string parameterName, string value, int expected)
        {
            // Act
            var binding = BindQueryValue(parameterName, value);

            // Assert
            Assert.True(binding.Result.IsModelSet);
            Assert.Equal(expected, binding.Result.Model);
            Assert.Equal(0, binding.ModelState.ErrorCount);
        }

        // Tests: the [Range] attributes reject page and perPage below 1, and accept 1.
        // (perPage's upper limit comes from configuration and is tested in CQCControllerTests.)
        [Theory]
        [InlineData("page", 0, false)]
        [InlineData("page", 1, true)]
        [InlineData("page", -2, false)]
        [InlineData("perPage", 0, false)]
        [InlineData("perPage", 1, true)]
        [InlineData("perPage", -2, false)]
        public void PagingValue_Range_IsChecked(string parameterName, int value, bool expectedValid)
        {
            // Arrange
            var range = GetProvidersParameter(parameterName).GetCustomAttribute<RangeAttribute>();

            // Act
            var isValid = range!.IsValid(value);

            // Assert
            Assert.Equal(expectedValid, isValid);
        }
    }
}