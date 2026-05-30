using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Page_API.Authentication;
using Page_API.Models;
using Page_API.Services;
using System.Net;

namespace Page_API.Controllers
{
    [ApiController]
    [Authorize(Policy = AdminPolicies.AdminOnly)]
    [Route("api/page")]
    [Produces("application/json")]
    public class PageController : ControllerBase
    {
        private readonly IFacebookService _facebookService;

        public PageController(IFacebookService facebookService)
        {
            _facebookService = facebookService;
        }

        private IActionResult HandleFacebookException(FacebookApiException ex)
        {
            var fb = ex.FacebookError;
            var details = fb is null
                ? ex.Message
                : $"{fb.Type} (code {fb.Code}, subcode {fb.ErrorSubcode}, trace {fb.FbtraceId}): {fb.Message}";

            if (fb?.Code == 190)
            {
                return StatusCode(
                    StatusCodes.Status401Unauthorized,
                    ApiResponse<object>.Fail(
                        $"Facebook access token khong hop le hoac da het han. Hay cap nhat cau hinh Facebook:PageAccessToken. {details}",
                        "FACEBOOK_TOKEN_INVALID"));
            }

            if ((int)ex.UpstreamStatusCode >= 500)
            {
                return StatusCode(
                    StatusCodes.Status502BadGateway,
                    ApiResponse<object>.Fail(details, "FACEBOOK_UPSTREAM_ERROR"));
            }

            var status = ex.UpstreamStatusCode == 0 ? HttpStatusCode.BadRequest : ex.UpstreamStatusCode;
            return StatusCode((int)status, ApiResponse<object>.Fail(details, "FACEBOOK_API_ERROR"));
        }

        [HttpGet("{pageId}")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetPageInfo(string pageId)
        {
            try
            {
                var result = await _facebookService.GetPageInfoAsync(pageId);
                return Ok(ApiResponse<object?>.Ok(result, "Page information retrieved successfully."));
            }
            catch (FacebookApiException ex)
            {
                return HandleFacebookException(ex);
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message, "BACKEND_ERROR"));
            }
        }

        [HttpGet("{pageId}/posts")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetPosts(string pageId)
        {
            try
            {
                var result = await _facebookService.GetPostsAsync(pageId);
                return Ok(ApiResponse<object?>.Ok(result, "Posts retrieved successfully."));
            }
            catch (FacebookApiException ex)
            {
                return HandleFacebookException(ex);
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message, "BACKEND_ERROR"));
            }
        }

        [HttpPost("{pageId}/posts")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> CreatePost(string pageId, [FromBody] CreatePostRequest request)
        {
            try
            {
                var result = await _facebookService.CreatePostAsync(pageId, request);
                return Ok(ApiResponse<object?>.Ok(result, "Post created successfully."));
            }
            catch (FacebookApiException ex)
            {
                return HandleFacebookException(ex);
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message, "BACKEND_ERROR"));
            }
        }

        [HttpDelete("post/{postId}")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> DeletePost(string postId)
        {
            try
            {
                var success = await _facebookService.DeletePostAsync(postId);
                if (success)
                {
                    return Ok(ApiResponse<object?>.Ok(null, "Post deleted successfully."));
                }

                return BadRequest(ApiResponse<object>.Fail("Failed to delete post.", "FACEBOOK_DELETE_FAILED"));
            }
            catch (FacebookApiException ex)
            {
                return HandleFacebookException(ex);
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message, "BACKEND_ERROR"));
            }
        }

        [HttpGet("post/{postId}/comments")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetComments(string postId)
        {
            try
            {
                var result = await _facebookService.GetCommentsAsync(postId);
                return Ok(ApiResponse<object?>.Ok(result, "Comments retrieved successfully."));
            }
            catch (FacebookApiException ex)
            {
                return HandleFacebookException(ex);
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message, "BACKEND_ERROR"));
            }
        }

        [HttpGet("post/{postId}/likes")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetLikes(string postId)
        {
            try
            {
                var result = await _facebookService.GetLikesAsync(postId);
                return Ok(ApiResponse<object?>.Ok(result, "Likes retrieved successfully."));
            }
            catch (FacebookApiException ex)
            {
                return HandleFacebookException(ex);
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message, "BACKEND_ERROR"));
            }
        }

        [HttpGet("{pageId}/insights")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetInsights(string pageId)
        {
            try
            {
                var result = await _facebookService.GetInsightsAsync(pageId);
                return Ok(ApiResponse<object?>.Ok(result, "Insights retrieved successfully."));
            }
            catch (FacebookApiException ex)
            {
                return HandleFacebookException(ex);
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message, "BACKEND_ERROR"));
            }
        }
    }
}
