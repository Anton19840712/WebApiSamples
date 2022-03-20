using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;
using SkiShift.Application.DTOs;
using SkiShift.Code.Services;
using SkiShift.Code.Services.ProfileManagement;
using SkiShift.Constants;
using SkiShift.Models.CommonModels;
using SkiShift.Models.DTOs;
using SkiShift.Models.Enums;
using SkiShift.Models.RequestFileByTypeModels;
using SkiShift.Models.ResponseModels;
using SkiShift.Models.StorageModels;
using SkiShift.Models.StorageModels.MongoModels;
using Swashbuckle.AspNetCore.Annotations;
//DealSpecificController
namespace SkiShift.Controllers
{
    [ApiController]
    [ApiVersion(ApiConstants.Versions.V1_0)]
    [Route(Controller)]
    [SwaggerTag("Create, read, update and delete Deal")]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public class DealSpecificController : ControllerBase
    {
        #region private fields
        private dynamic _ret;
        private HttpStatusCode _code;
        private readonly IDealService _dealService;
        private readonly ILogger<DealSpecificController> _logger;
        private readonly UnitOfWork _unitOfWork;
        private const string Controller = ApiConstants.Endpoints.DefaultVersionedRoute + "dealSpecific/";
        private readonly IProfileService<BaseContextRequest> _profileConService;
        private readonly IMapper _autoMapper;
        #endregion

        #region constructor
        public DealSpecificController(
            IDealService dealService, 
            ILogger<DealSpecificController> logger, 
            UnitOfWork unitOfWork, 
            IProfileService<BaseContextRequest> profileConService, 
            IMapper autoMapper)
        {
            _dealService = dealService;
            _logger = logger;
            _unitOfWork = unitOfWork;
            _profileConService = profileConService;
            _autoMapper = autoMapper;
        }
        #endregion

        #region refreshIfTimeIsUpOrOnMyWay
        /// <summary>
        /// Endpoint changes statuses in deals using Hang fire tool. 
        /// </summary>
        /// <returns></returns>
        [HttpPost]
        [Route("refreshIfTimeIsUpOrOnMyWay")]
        [SwaggerOperation(Summary = "Update deal data.", Description = "Update offer in deal, when time is gone or on my way.")]
        [Authorize(Roles = "b2b, admin, client")]
        public async Task<ActionResult> RefreshIfTimeIsUpOrOnMyWay()
        {
            return await _dealService.UpdateDealsStatus();
        }
        #endregion

        /// <summary>
        /// Endpoint returns all deals by statuses.
        /// </summary>
        /// <param name="statuses"></param>
        /// <returns></returns>
        #region getAllDealsByStatuses
        [HttpGet]
        [SwaggerOperation(Summary = "Get all deals ", Description = "Get all deals by parameters of status")]
        [Authorize(Roles = "b2b, admin, client")]
        [Route("getAllDealsByStatuses")]
        public async Task<ActionResult<List<DealResponseDto>>> GetAllDealsByStatuses([FromBody] List<DealStatuses> statuses)
        {
            //var testUserId = "caaae14e-622b-4b0c-bade-c95ae580debb";
            var sessionUserId = (await _profileConService.GetCurrentUserFromTheDatabase(HttpContext?.User)).Id.ToString();

            var filter1 = Builders<DealDalDto>.Filter.Eq("offers.clientId", sessionUserId);

            if (statuses.Any())
            {
                try
                {
                    var filter2 = Builders<DealDalDto>.Filter.In("dealStatus", statuses);

                    var commonFilterDealIsOver = Builders<DealDalDto>.Filter.And(new List<FilterDefinition<DealDalDto>> { filter1, filter2 });

                    _ret = _unitOfWork.GetCollection<DealDalDto>("Deal").FindAsync(commonFilterDealIsOver).Result
                        .ToList().Select(x => _autoMapper.Map<DealResponseDto>(x)).ToList(); ;

                    _code = HttpStatusCode.OK;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.StackTrace);

                    _ret = new { err = ex.ToString() };

                    _code = HttpStatusCode.InternalServerError;

                    throw;
                }
            }
            else
            {
                try
                {
                    _ret = _unitOfWork.GetCollection<DealDalDto>("Deal").FindAsync(filter1).Result
                        .ToList().Select(x => _autoMapper.Map<DealResponseDto>(x)).ToList();

                    _code = HttpStatusCode.OK;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.StackTrace);

                    _ret = new { err = ex.ToString() };

                    _code = HttpStatusCode.InternalServerError;

                    throw;
                }
            }


            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }
        #endregion

        #region updateFullLoadInDeal

        /// <summary>
        /// Endpoint update full load status in deals.
        /// </summary>
        /// <param name="dealDtoFromUi"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("updateFullLoadInDeal")]
        [SwaggerOperation(Summary = "Update offer in deal", Description = "Update offer in deal")]
        [Authorize(Roles = "b2b, admin, client")]
        public async Task<ActionResult<string>> UpdateFullLoadInDeal([FromBody] DealRequestDto dealDtoFromUi)
        {
            try
            {
                if (dealDtoFromUi != null)
                {
                    //1) searching...:
                    var filterToFindDeal = Builders<DealDalDto>.Filter.Eq("_id", dealDtoFromUi.DealId);

                    var dealObjectFromDataBase = await _unitOfWork.GetCollection<DealDalDto>("Deal").Find(filterToFindDeal).FirstOrDefaultAsync();

                    //2) setting, updating fields...:
                    if (dealObjectFromDataBase != null)
                    {
                        await _dealService.UpdateOfferAndDealStatuses(dealObjectFromDataBase, dealDtoFromUi);
                    }
                    
                    _ret = "All related offers have been updated.";

                    _code = HttpStatusCode.OK;

                }
            }

            catch (Exception exception)
            {
                _logger.LogError(exception.StackTrace);

                _ret = new { err = exception.ToString() };

                _code = HttpStatusCode.InternalServerError;

                throw;
            }

            return new ObjectResult(_ret) { StatusCode = (int)_code };
        }

        #endregion

        /// <summary>
        /// Endpoint changes deal status.
        /// </summary>
        /// <param name="dealId"></param>
        /// <param name="offerId"></param>
        /// <returns></returns>
        #region cancelByShipper
        [HttpPost]
        [Route("cancelByShipper")]
        [SwaggerOperation(Summary = "Cancel offer by offer id in deal", Description = "Cancel offer in deal")]
        [Authorize(Roles = "b2b, admin, client")]
        public async Task<ActionResult<string>> CancelByShipper(
            [FromQuery] Guid dealId,
            [FromQuery] Guid offerId)
        {
            try
            {
                if (dealId != Guid.Empty)
                {
                    //1) searching...:
                    var dealObjectFromDataBase = await _dealService.FindDealByDealId(dealId);

                    //2) setting, updating fields...:
                    if (dealObjectFromDataBase != null)
                    {
                        dealObjectFromDataBase?.Offers.ForEach(x => { if (x.Id == offerId) { x.OfferStatus = OfferStatuses.DisagreeShifter; } });

                        try
                        {
                            await _dealService.UpdateById(dealId, dealObjectFromDataBase);
                        }

                        catch (Exception exception)
                        {
                            _logger.LogError(exception.StackTrace);

                            _ret = new { err = exception.ToString() };

                            throw new Exception();
                        }
                    }

                    _ret = "Offer status in deal was set to cancelled by shifter.";

                    _code = HttpStatusCode.OK;
                }
            }

            catch (Exception exception)
            {
                _logger.LogError(exception.StackTrace);

                _ret = new { err = exception.ToString() };

                _code = HttpStatusCode.InternalServerError;

                throw;
            }

            return new ObjectResult(_ret) { StatusCode = (int)_code };
        }

        #endregion

        #region finishDeal deal is over
        ///<summary> Sets statuses of the offer to "deal is over", "agree shifter"</summary>
        ///<response code="200">Update was successful</response>
        ///<response code="500">Unable to update</response>
        ///<param name="id">Id of the deal</param>
        [HttpGet]
        [Route("finishDeal")]
        [Produces("application/json")]
        public async Task<IActionResult> FinishDeal([FromQuery(Name = "DealId")] Guid id)
        {
            try
            {
                var filter = Builders<DealDalDto>.Filter.Eq("_id", id);

                var update = Builders<DealDalDto>.Update
                    .Set("dealStatus", DealStatuses.DealIsOver)//3
                    .Set("offers.$[].offerStatus", OfferStatuses.ConfirmedDeliveryShifter)//5
                    .Set("lastModified", DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss"));

                await _unitOfWork.GetCollection<DealDalDto>("Deal").UpdateOneAsync(filter, update);

                _ret = "The data in offers inside deal was updated.";

                _code = HttpStatusCode.OK;
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new { err = ex.ToString() };

                _code = HttpStatusCode.InternalServerError;

                throw;
            }
            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }

        #endregion

        /// <summary>
        /// Endpoint changes deal status.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        #region agreeAll
        [Route("agreeAll")]
        [HttpGet]
        [SwaggerOperation(
            Summary = "Button confirm was pressed",
            Description = "Statuses have changed to: offer agree all."
        )]
        public async Task<ObjectResult> AgreeAll([FromQuery(Name = "DealId")] Guid id)
        {
            try
            {
                //FILTER CRITERIA:
                var filter = Builders<DealDalDto>.Filter.Eq("_id", id);

                //UPDATE CRITERIA:
                var update = Builders<DealDalDto>.Update
                    .Set("dealStatus", DealStatuses.BeingFormed)//0
                    .Set("offers.$[].offerStatus", OfferStatuses.AgreeAll)//3
                    .Set("lastModified", DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss"));

                await _unitOfWork.GetCollection<DealDalDto>("Deal").UpdateOneAsync(filter, update);

                _ret = "The data in deal and its offers was updated.";

                _code = HttpStatusCode.OK;
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new { err = ex.ToString() };

                _code = HttpStatusCode.InternalServerError;

                throw;
            }
            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }
        #endregion

        /// <summary>
        /// Endpoint changes deal status.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="offerId"></param>
        /// <returns></returns>
        #region subscribedByClient
        [Route("subscribedByClient")]
        [HttpGet]
        [SwaggerOperation(
            Summary = "Button subscribe was pressed",
            Description = "Statuses have changed to: offer agreeClient"
        )]
        public async Task<ObjectResult> SubscribedByClient(
            [FromQuery(Name = "DealId")] Guid id,
            [FromQuery(Name = "OfferId")] Guid offerId)
        {
            try
            {
                var filter = Builders<DealDalDto>.Filter.Eq("_id", id);
                
                var update = Builders<DealDalDto>.Update
                    .Set("dealStatus", DealStatuses.DealIsCrash)//0
                    .Set("offers.$[f].offerStatus", OfferStatuses.AgreeClient)//11
                    .Set("offers.$[f].lastModified", DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss"));
                
                var arrayFilters = new[]
                {
                    new BsonDocumentArrayFilterDefinition<BsonDocument>(new BsonDocument("f.offerId", new BsonDocument("$eq", offerId.ToString())))
                };

                await _unitOfWork.GetCollection<DealDalDto>("Deal")
                    .FindOneAndUpdateAsync(filter, update, new FindOneAndUpdateOptions<DealDalDto> { ArrayFilters = arrayFilters });


                _ret = "The data in deal and its offer was updated";

                _code = HttpStatusCode.OK;
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new { err = ex.ToString() };

                _code = HttpStatusCode.InternalServerError;

                throw;
            }
            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }

        #endregion

        /// <summary>
        /// Endpoint changes deal status.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        #region subscribedByShifter
        [Route("subscribedByShifter")]
        [HttpGet]
        [SwaggerOperation(
            Summary = "Button subscribe was pressed",
            Description = "Statuses have changed to: offer agreeShifter"
        )]
        public async Task<ObjectResult> SubscribedByShipper([FromQuery(Name = "DealId")] string id)
        {
            try
            {
                //FILTER CRITERIA:
                var filter = Builders<DealDalDto>.Filter.Eq("_id", id);

                //UPDATE CRITERIA:
                var update = Builders<DealDalDto>.Update
                    .Set("dealStatus", DealStatuses.BeingFormed)//0
                    .Set("offers.$[].offerStatus", OfferStatuses.AgreeShifter)//1
                    .Set("lastModified", DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss"));

                await _unitOfWork.GetCollection<DealDalDto>("Deal").UpdateOneAsync(filter, update);

                _ret = "The data in deal and its offers was updated.";

                _code = HttpStatusCode.OK;
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new { err = ex.ToString() };

                _code = HttpStatusCode.InternalServerError;

                throw;
            }
            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }
        #endregion

        /// <summary>
        /// Endpoint changes deal status.
        /// </summary>
        /// <param name="dealId"></param>
        /// <param name="offerId"></param>
        /// <returns></returns>
        #region shipperParcelCancel
        [Route("shifterParcelCancel")]
        [HttpGet]
        [SwaggerOperation(
            Summary = "Button cancel parcel was pressed by shipper.",
            Description = "Statuses have changed to: offer cancelShifter."
        )]
        public async Task<ObjectResult> ShifterParcelCancel(
            [FromQuery(Name = "DealId")] Guid dealId,
            [FromQuery(Name = "OfferId")] string offerId)
        {
            try
            {
                var filter = Builders<DealDalDto>.Filter.Eq("_id", dealId);

                var update = Builders<DealDalDto>.Update
                    .Set("dealStatus", DealStatuses.BeingFormed)//0
                    .Set("offers.$[f].offerStatus", OfferStatuses.CancelShifter)//12
                    .Set("offers.$[f].lastModified", DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss"));

                var arrayFilters = new[]
                {
                    new BsonDocumentArrayFilterDefinition<BsonDocument>(new BsonDocument("f._id", new BsonDocument("$eq", offerId)))
                };

                await _unitOfWork.GetCollection<DealDalDto>("Deal")
                    .FindOneAndUpdateAsync(filter, update, new FindOneAndUpdateOptions<DealDalDto> { ArrayFilters = arrayFilters });

                _ret = "The data in deal and its offers was updated by offer id.";

                _code = HttpStatusCode.OK;
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new { err = ex.ToString() };

                _code = HttpStatusCode.InternalServerError;

                throw;
            }
            var result = new ObjectResult(_ret) { StatusCode = (int)_code };
            return result;
        }
        #endregion

        /// <summary>
        /// Endpoint changes deal status.
        /// </summary>
        /// <param name="dealId"></param>
        /// <param name="parcelId"></param>
        /// <returns></returns>
        #region clientParcelCancel
        [Route("clientParcelCancel")]
        [HttpGet]
        [SwaggerOperation(
            Summary = "Button cancel parcel was pressed by client.",
            Description = "Statuses have changed to: offer cancelClient."
        )]
        public async Task<ObjectResult> ClientParcelCancel(
            [FromQuery(Name = "DealId")] Guid dealId,
            [FromQuery(Name = "ParcelId")] string parcelId
        )
        {
            try
            {
                var filter = Builders<DealDalDto>.Filter.Eq("_id", dealId);

                var update = Builders<DealDalDto>.Update
                    .Set("dealStatus", DealStatuses.BeingFormed)//0
                    .Set("offers.$[f].offerStatus", OfferStatuses.CancelClient)//11
                    .Set("offers.$[f].lastModified", DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss"));

                var arrayFilters = new[]
                {
                    new BsonDocumentArrayFilterDefinition<BsonDocument>(new BsonDocument("f.parcelId", new BsonDocument("$eq", parcelId)))
                };

                await _unitOfWork.GetCollection<DealDalDto>("Deal")
                        .FindOneAndUpdateAsync(filter, update, new FindOneAndUpdateOptions<DealDalDto> { ArrayFilters = arrayFilters });

                _ret = "The data in deal and its offers was updated by parcel id.";

                _code = HttpStatusCode.OK;
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new { err = ex.ToString() };

                _code = HttpStatusCode.InternalServerError;

                throw;
            }
            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }
        #endregion

        /// <summary>
        /// Endpoint changes deal status.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="offerId"></param>
        /// <returns></returns>
        #region didNotReceiveFromClient
        [Route("didNotReceiveFromClient")]
        [HttpGet]
        [SwaggerOperation(
            Summary = "Button didNotReceiveFromClient was pressed by client.",
            Description = "Statuses have changed to: offer is dealIsOver, disagreeClient."
        )]
        public async Task<ObjectResult> DidNotReceiveFromClient(
            [FromQuery(Name = "DealId")] Guid id,
            [FromQuery(Name = "OfferId")] Guid offerId
        )
        {
            try
            {
                var filter = Builders<DealDalDto>.Filter.Eq("_id", id);

                var update = Builders<DealDalDto>.Update
                    .Set("dealStatus", DealStatuses.DealIsOver)//3
                    .Set("offers.$[f].offerStatus", OfferStatuses.DisagreeClient)//8
                    .Set("offers.$[f].lastModified", DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss"));

                var arrayFilters = new[]
                {
                    new BsonDocumentArrayFilterDefinition<BsonDocument>(new BsonDocument("f._id", new BsonDocument("$eq", offerId.ToString())))
                };

                await _unitOfWork.GetCollection<DealDalDto>("Deal")
                    .FindOneAndUpdateAsync(filter, update, new FindOneAndUpdateOptions<DealDalDto> { ArrayFilters = arrayFilters });

                _ret = "The data in offer was updated.";

                _code = HttpStatusCode.OK;
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new { err = ex.ToString() };

                _code = HttpStatusCode.InternalServerError;

                throw;
            }
            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }
        #endregion

        /// <summary>
        /// Endpoint changes deal status.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        #region crashDealByShipper
        [Route("crashDealByShipper")]
        [HttpGet]
        [SwaggerOperation(
            Summary = "Button crash deal was pressed by shipper.",
            Description = "Statuses have changed to: offer is dealIsCrash, cancelShifter."
        )]
        public async Task<ObjectResult> CrashDealByShipper([FromQuery(Name = "DealId")] string id)
        {
            try
            {
                //FILTER CRITERIA:
                var filter = Builders<DealDalDto>.Filter.Eq("_id", id);

                //UPDATE CRITERIA:
                var update = Builders<DealDalDto>.Update
                    .Set("dealStatus", DealStatuses.DealIsCrash)//5
                    .Set("offers.$[].offerStatus", OfferStatuses.CancelShifter)//12
                    .Set("offers.$[].lastModified", DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss"));

                await _unitOfWork.GetCollection<DealDalDto>("Deal").UpdateOneAsync(filter, update);

                _ret = "The data in deal and its offers was updated.";

                _code = HttpStatusCode.OK;
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new { err = ex.ToString() };

                _code = HttpStatusCode.InternalServerError;

                throw;
            }
            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }
        #endregion

        /// <summary>
        /// Endpoint changes deal status.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        #region cancelDealByShipper
        [Route("cancelDealByShipper")]
        [HttpGet]
        [SwaggerOperation(
            Summary = "Button cancel deal was pressed by shipper.",
            Description = "Statuses have changed to: offer is dealIsCancel, cancelShifter."
        )]
        public async Task<ObjectResult> CancelDealByShipper([FromQuery(Name = "DealId")] string id)
        {
            try
            {
                //FILTER CRITERIA:
                var filter = Builders<DealDalDto>.Filter.Eq("_id", id);

                //UPDATE CRITERIA:
                var update = Builders<DealDalDto>.Update
                    .Set("dealStatus", DealStatuses.DealIsCancel)//4
                    .Set("offers.$[].offerStatus", OfferStatuses.CancelShifter)//12
                    .Set("offers.$[].lastModified", DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss"));

                await _unitOfWork.GetCollection<DealDalDto>("Deal").UpdateOneAsync(filter, update);

                _ret = "The data in deal and its offers was updated.";

                _code = HttpStatusCode.OK;
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new { err = ex.ToString() };

                _code = HttpStatusCode.InternalServerError;

                throw;
            }
            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }
        #endregion

        /// <summary>
        /// Endpoint changes deal status.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="offerId"></param>
        /// <returns></returns>
        #region disagreeClient
        [Route("disagreeClient")]
        [HttpGet]
        [SwaggerOperation(
            Summary = "Button disagree was pressed by client.",
            Description = "Statuses have changed to: offer disagreeClient."
        )]
        public async Task<ObjectResult> DisagreeClient(
            [FromQuery(Name = "DealId")] Guid id,
            [FromQuery(Name = "OfferId")] Guid offerId)
        {
            try
            {
                //FILTER CRITERIA:
                var filter = Builders<DealDalDto>.Filter.Eq("_id", id);

                //UPDATE CRITERIA:
                var update = Builders<DealDalDto>.Update
                    .Set("dealStatus", DealStatuses.BeingFormed)//0
                    .Set("offers.$[f].offerStatus", OfferStatuses.DisagreeClient)//8
                    .Set("offers.$[f].lastModified", DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss"));

                var arrayFilters = new[]
                {
                    new BsonDocumentArrayFilterDefinition<BsonDocument>(new BsonDocument("f._id", new BsonDocument("$eq", offerId.ToString())))
                };

                await _unitOfWork.GetCollection<DealDalDto>("Deal")
                    .FindOneAndUpdateAsync(filter, update, new FindOneAndUpdateOptions<DealDalDto> { ArrayFilters = arrayFilters });


                _ret = "The data in deal and its offer was updated.";

                _code = HttpStatusCode.OK;
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new { err = ex.ToString() };

                _code = HttpStatusCode.InternalServerError;

                throw;
            }
            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }
        #endregion

        /// <summary>
        /// Endpoint changes deal status.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        #region disagreeShifter
        [Route("disagreeShifter")]
        [HttpGet]
        [SwaggerOperation(
            Summary = "Button disagree was pressed by shifter.",
            Description = "Statuses have changed to: offer disagreeShifter."
        )]
        public async Task<ObjectResult> DisagreeShifter([FromQuery(Name = "DealId")] string id)
        {
            try
            {
                //FILTER CRITERIA:
                var filter = Builders<DealDalDto>.Filter.Eq("_id", id);

                //UPDATE CRITERIA:
                var update = Builders<DealDalDto>.Update
                    .Set("dealStatus", DealStatuses.BeingFormed)//0
                    .Set("offers.$[].offerStatus", OfferStatuses.DisagreeShifter)//9
                    .Set("offers.$[].lastModified", DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss"));

                await _unitOfWork.GetCollection<DealDalDto>("Deal").UpdateOneAsync(filter, update);

                _ret = "The data in deal and its offers was updated.";

                _code = HttpStatusCode.OK;
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new { err = ex.ToString() };

                _code = HttpStatusCode.InternalServerError;

                throw;
            }
            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }
        #endregion

        #region shipperCreatesOffer
        ///<summary>Shifter created offer in existed deal.</summary>
        ///<description>New offer is created. Status of the offer set to agree shipper.</description>
        ///<response code="201">Status updated.</response>
        ///<response code="500">Unable to update.</response>
        ///<param name="request">Sample request.</param>
        ///<remarks>Sample **request**: POST /</remarks>
        [HttpPost]
        [Route("shipperCreatesOffer")]
        public async Task<ObjectResult> CreateOfferFromShipperSide([FromForm] HttpContextRequestDto request)
        {
            var offerPlusModel = _autoMapper.Map<OfferPlusRequest>(request);

            var offerToCreate = _autoMapper.Map<OfferDalDto>(offerPlusModel);

            try
            {
                var filter = Builders<DealDalDto>.Filter.Eq("_id", offerPlusModel.DealId);

                offerToCreate.LastModified = DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss");

                offerToCreate.OfferStatus = OfferStatuses.AgreeShifter;

                offerToCreate.WhoCreator = WhoCreator.ShifterFirst;

                var update = Builders<DealDalDto>.Update.AddToSet(x => x.Offers, offerToCreate);

                await _unitOfWork.GetCollection<DealDalDto>("Deal").UpdateOneAsync(filter, update);

                _ret = "Shifter has created offer.";

                _code = HttpStatusCode.Created;
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new { err = ex.ToString() };

                _code = HttpStatusCode.InternalServerError;

                throw;
            }
            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }
        #endregion

        #region refuseFromTheDeal
        ///<summary>Statuses have changed to: offer in deal to cancel shifter. Shifter refused from the deal.</summary>
        ///<response code="200">Status updated.</response>
        ///<response code="500">Unable to update.</response>
        ///<param name="id">Deal id parameter.</param>
        [HttpGet]
        [Route("refuseFromTheDeal")]
        public async Task<ObjectResult> RefuseFromTheDeal([FromQuery(Name = "DealId")] Guid id)
        {
            try
            {
                var filter = Builders<DealDalDto>.Filter.Eq("_id", id);

                var update = Builders<DealDalDto>.Update
                    .Set("offers.$[].offerStatus", OfferStatuses.CancelShifter)//12
                    .Set("offers.$[].lastModified", DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss"));

                await _unitOfWork.GetCollection<DealDalDto>("Deal").UpdateOneAsync(filter, update);

                _ret = "Refused from the deal.";

                _code = HttpStatusCode.OK;
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new { err = ex.ToString() };

                _code = HttpStatusCode.InternalServerError;

                throw;
            }
            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }
        #endregion

        #region clientSubscribedToDealAgain
        ///<summary>Client agree again. Statuses have changed to: offer in deal to agree client.</summary>
        ///<response code="200">Status updated.</response>
        ///<response code="500">Unable to update.</response>
        ///<param name="dealId">Id of the deal.</param>
        ///<param name="offerId">Id of the offer.</param>
        [HttpGet]
        [Route("clientSubscribedToDealAgain")]
        public async Task<ObjectResult> ClientSubscribedToDealAgain([FromQuery(Name = "DealId")] Guid dealId, [FromQuery(Name = "OfferId")] Guid offerId)
        {
            try
            {

                var filter = Builders<DealDalDto>.Filter.Eq("_id", dealId);

                var update = Builders<DealDalDto>.Update
                    .Set("offers.$[f].offerStatus", OfferStatuses.AgreeClient)
                    .Set("offers.$[f].lastModified", DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss"));

                var arrayFilters = new[] { new BsonDocumentArrayFilterDefinition<BsonDocument>(new BsonDocument("f._id", new BsonDocument("$eq", offerId.ToString()))) };

                var resultOfStatusChanging = (await _unitOfWork.GetCollection<DealDalDto>("Deal")
                        .FindOneAndUpdateAsync(filter, update, new FindOneAndUpdateOptions<DealDalDto> { ReturnDocument = ReturnDocument.After, ArrayFilters = arrayFilters }))
                        .Offers
                        .FirstOrDefault(x => x.Id == offerId)?
                        .OfferStatus;

                _ret = $"Offer status of was updated to {resultOfStatusChanging}.";

                _code = HttpStatusCode.OK;
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new { err = ex.ToString() };

                _code = HttpStatusCode.InternalServerError;

                throw;
            }
            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }
        #endregion

        #region subscribeToTheDealWithANewParcel
        ///<summary>Client subscribe to the deal with the next parcel</summary>
        ///<response code="201">New offer created.</response>
        ///<response code="500">Unable to update.</response>
        ///<param name="request">Sample request parameter.</param>
        [Obsolete]
        [HttpPost]
        [Route("subscribeToTheDealWithANewParcel")]
        public async Task<ObjectResult> SubscribeToTheDealWithANewParcel([FromForm] HttpContextRequestDto request)
        {
            var offerPlusModel = _autoMapper.Map<OfferPlusRequest>(request);

            var offerToCreate = _autoMapper.Map<OfferDalDto>(offerPlusModel);

            try
            {
                var filter = Builders<DealDalDto>.Filter.Eq("_id", offerPlusModel.DealId);

                offerToCreate.LastModified = DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss");

                offerToCreate.OfferStatus = OfferStatuses.AgreeClient;

                offerToCreate.WhoCreator = WhoCreator.ClientFirst;//1

                var update = Builders<DealDalDto>.Update.AddToSet(x => x.Offers, offerToCreate);

                await _unitOfWork.GetCollection<DealDalDto>("Deal").UpdateOneAsync(filter, update);

                _ret = "Client subscribed to the deal by parcel.";

                _code = HttpStatusCode.Created;
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new { err = ex.ToString() };

                _code = HttpStatusCode.InternalServerError;

                throw;
            }
            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }
        #endregion

        #region subscribeToTheDealByParcelFromClientSide
        ///<summary>Client agree again. Statuses have changed to: offer in deal to agree client.</summary>
        ///<response code="201">Status updated.</response>
        ///<response code="500">Unable to update.</response>
        ///<param name="request">Sample request parameter.</param>
        [HttpPost]
        [Route("subscribeToTheDealByParcelFromClientSide")]
        public async Task<dynamic> SubscribeToTheDealByParcelFromClientSide([FromForm] HttpContextRequestDto request)
        {
            var offerPlusModel = _autoMapper.Map<OfferPlusRequest>(request);

            var offerToCreate = _autoMapper.Map<OfferDalDto>(offerPlusModel);

            try
            {
                //1st database request: updating existed deal with a new created offer...:
                var filter = Builders<DealDalDto>.Filter.Eq("_id", offerPlusModel.DealId);

                offerToCreate.LastModified = DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss");

                offerToCreate.OfferStatus = OfferStatuses.AgreeClient;

                offerToCreate.WhoCreator = WhoCreator.ClientFirst;

                var update = Builders<DealDalDto>.Update.AddToSet(x => x.Offers, offerToCreate);

                //getting model back after update...:
                var dealModel = await _unitOfWork.GetCollection<DealDalDto>("Deal")
                    .FindOneAndUpdateAsync(filter, update, new FindOneAndUpdateOptions<DealDalDto> { ReturnDocument = ReturnDocument.After });

                //collecting ids of clients from the deal...:
                var clientIdsFromCreatedOffersCollection = dealModel.Offers.Select(x => x.ClientId).Distinct().ToList();
                
                //2nd database request : collecting user profiles by ids from user profile db table ...:
                var filterOfIds = Builders<UserProfile>.Filter.In("_id", clientIdsFromCreatedOffersCollection);

                var userProfilesCollection = await _unitOfWork.GetCollection<UserProfile>("UserProfile")
                        .FindAsync(filterOfIds)
                        .Result
                        .ToListAsync();

                //collecting offers from the deal...:
                var offerElementsCollection = dealModel.Offers;

                //creating new model from collections to return to UI...:
                var resultingCollection
                    = userProfilesCollection
                        .GroupJoin(offerElementsCollection, userProfile => userProfile.Id,
                            offer => offer.ClientId, (userProfile, offersCollection) =>
                                new DealAndUserProfileModel()
                                {
                                    Id = dealModel.Id,
                                    TotalSum = offerElementsCollection.Select(x => x.OfferSum).Sum() ?? 0.00,
                                    Description = dealModel.DealDescription,
                                    DealStatus = dealModel.DealStatus,
                                    LastModified = dealModel.LastModified,
                                    ShifterId = dealModel.ShifterId,
                                    TelephoneNumber = userProfile.UserName,
                                    FirstName = userProfile.FirstName,
                                    SecondName = userProfile.SecondName,
                                    Documents = userProfile.Documents,
                                    ClientOffers = offersCollection.Select(x => _autoMapper.Map<OfferResponseDto>(x)).ToList()
                                }
                            ).ToList();

                _ret = resultingCollection;

                _code = HttpStatusCode.Created;
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new { err = ex.ToString() };

                _code = HttpStatusCode.InternalServerError;

                throw;
            }

            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }
        #endregion

        #region searchOfferById
        /// <summary>
        /// Endpoint changes deal status.
        /// </summary>
        /// <param name="offerId"></param>
        /// <returns></returns>
        [Route("searchOfferById")]
        [HttpGet]
        [SwaggerOperation(
            Summary = "Search offer by id.",
            Description = "Search offer by id."
        )]
        public async Task<ObjectResult> FindOfferById([FromQuery(Name = "OfferId")] Guid offerId)
        {
            try
            {
                var filter = Builders<DealDalDto>.Filter.Eq(
                    "offers._id", offerId);

                var offer = (await _unitOfWork.GetCollection<DealDalDto>("Deal")
                    .FindAsync(filter)).FirstOrDefaultAsync().Result.Offers.FirstOrDefault(x=>x.Id == offerId);

                if (offer!=null)
                {
                    _ret = _autoMapper.Map<OfferResponseDto>(offer);

                    _code = HttpStatusCode.OK;
                }
                else
                {
                    _ret = JsonConvert.SerializeObject(new ResponseNoSuccess());

                    _code = HttpStatusCode.NoContent;
                }
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new { err = ex.ToString() };

                _code = HttpStatusCode.InternalServerError;

                throw;
            }
            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }
        #endregion

        #region secondSideAgrees
        [Route("secondSideAgrees")]
        [HttpGet]
        [SwaggerOperation(
            Summary = "Button subscribe was pressed",
            Description = "Statuses have changed to: offer agree all"
        )]
        public async Task<ObjectResult> SubscribeParcelToDeal(
            [FromQuery(Name = "DealId")] Guid id,
            [FromQuery(Name = "OfferId")] Guid offerId)
        {
            try
            {
                var filter = Builders<DealDalDto>.Filter.Eq("_id", id);

                var update = Builders<DealDalDto>.Update
                    .Set("dealStatus", DealStatuses.BeingFormed)//0
                    .Set("offers.$[f].offerStatus", OfferStatuses.AgreeAll)
                    .Set("lastModified", DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss"));

                var arrayFilters = new[] { new BsonDocumentArrayFilterDefinition<BsonDocument>(new BsonDocument("f._id", new BsonDocument("$eq", offerId.ToString()))) };

                var offerDal = (await _unitOfWork.GetCollection<DealDalDto>("Deal")
                    .FindOneAndUpdateAsync(filter, update, new FindOneAndUpdateOptions<DealDalDto> { ArrayFilters = arrayFilters }))
                    .Offers.FirstOrDefault(x => x.Id == offerId);

                //subscription mechanism...:
                var filterParcelDal = Builders<ParcelDalDto>.Filter.Eq("_id", id);

                var updateParcel = Builders<ParcelDalDto>.Update
                    .Set("offerId", offerDal?.Id)
                    .Set("lastModified", DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss"));

                await _unitOfWork.GetCollection<ParcelDalDto>("Parcel")
                            .FindOneAndUpdateAsync(filterParcelDal,
                            updateParcel, new FindOneAndUpdateOptions<ParcelDalDto> { ReturnDocument = ReturnDocument.After });

                _ret = JsonConvert.SerializeObject(new ResponseSuccess());

                _code = HttpStatusCode.OK;
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new { err = ex.ToString() };

                _code = HttpStatusCode.InternalServerError;

                throw;
            }
            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }
        #endregion
    }
}
