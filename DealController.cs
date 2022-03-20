using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using SkiShift.Application.DTOs;
using SkiShift.Code.Services;
using SkiShift.Models.Enums;
using Swashbuckle.AspNetCore.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SkiShift.Code.Services.ProfileManagement;
using SkiShift.Constants;
using SkiShift.Models.DTOs;
using SkiShift.Models.RequestFileByTypeModels;
using SkiShift.Models.ResponseModels;
using SkiShift.Models.StorageModels;
using SkiShift.Models.StorageModels.MongoModels;
using SkiShift.Validation.Extensions;
using Telegram.Bot;

namespace SkiShift.Controllers
{

    [ApiController]
    [ApiVersion(ApiConstants.Versions.V1_0)]
    [Route(Controller)]
    [SwaggerTag("Create, read, update and delete Deal")]
    [Authorize(AuthenticationSchemes = "Bearer")]

    public class DealController : ControllerBase
    {
        #region Private fields

        private dynamic _ret;
        private HttpStatusCode _code;
        private readonly IDealService _dealService;
        private readonly ILogger<DealController> _logger;
        private readonly UnitOfWork _unitOfWork;
        private readonly IMapper _autoMapper;
        private readonly TelegramBotClient _telegramBot;
        private const string Controller = ApiConstants.Endpoints.DefaultVersionedRoute + "deal/";
        #endregion

        #region Constructor
        public DealController
        (
            IDealService dealService,
            ILogger<DealController> logger,
            UnitOfWork unitOfWork,
            IProfileService<BaseContextRequest> profileConService,
            IMapper autoMapper
            ) 
        {
            _dealService = dealService; 
            _logger = logger;
            _unitOfWork = unitOfWork; 
            _autoMapper = autoMapper; 
            _telegramBot = new TelegramBotClient("2003623788:AAHdHFgu3yG7hrTYMuTElcQNibMb-qbFcPM");
        }
        #endregion
        
        #region getAllDealsForAdmin
        /// <summary>
        /// Endpoint returns all deals for admin.
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("getAllForAdmin")]
        [SwaggerOperation(
            Summary = "Get all deals for admin",
            Description = "Returns the list of the deals")]
        public async Task<ActionResult<List<DealResponseDto>>> GetAllDealsForAdmin()
        {
            try
            {
                var dealDalCollection = await _unitOfWork.GetCollection<DealModelDalDto>("Deal").Find(_ => true).ToListAsync();
                
                if (dealDalCollection.Any())
                {
                    var userDalCollection = await _unitOfWork.GetCollection<UserProfileSegmentDalDto>("UserProfile").Find(_ => true).ToListAsync();

                    var responseModel = new List<DealWithUserFieldsDto>();

                    foreach (var dealDalModel in dealDalCollection)
                    {
                        var isShipperIdInUserProfiles = userDalCollection.Any(shipper => shipper.Id == dealDalModel.ShifterId);
                        
                        var offersWithExistedClients = dealDalModel.Offers
                            .Where(offerDalModel => userDalCollection.Select(userProfileAdminDto => userProfileAdminDto.Id)//id of clients from user profile
                                .Intersect(dealDalModel.Offers.Select(x => x.ClientId))//id of clients from offers
                                .Contains(offerDalModel.ClientId))
                            .ToList();

                        if (isShipperIdInUserProfiles && offersWithExistedClients.Any())
                        {
                            var dealForAdminModel = new DealWithUserFieldsDto();

                            offersWithExistedClients.ForEach(offerDalDto =>
                                dealForAdminModel.Offers.Add(
                                    _dealService.CreateOfferCollection(userDalCollection.FirstOrDefault(userAdminModel => 
                                            userAdminModel.Id == offerDalDto.ClientId), offerDalDto)));

                            var shifterFromUserProfileModel = userDalCollection.FirstOrDefault(x => x.Id == dealDalModel.ShifterId);

                            var element = _dealService.ComposeDealCollection(dealForAdminModel, dealDalModel,
                                shifterFromUserProfileModel);

                            responseModel.Add(element);
                        }
                    }
                    
                    _ret = responseModel;

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

        #region updateTheDeal
        /// <summary>
        /// Endpoint updates the deal by id.
        /// </summary>
        /// <param name="dealModel"></param>
        /// <returns></returns>
        [HttpPatch]
        [Route("updateTheDeal")]
        [SwaggerOperation(Summary = "Update Deal", Description = "Update Deal")]
        public async Task<ActionResult<DealResponseDto>> UpdateTheDeal([FromBody] DealPatchDto dealModel)
        {
            var transportValidationResult = dealModel.Transport.ToString().ValidateTransportType();

            if (transportValidationResult!=Transport.Failed)
            {
                var dealToPatch = _autoMapper.Map<DealDalDto>(dealModel);

                try
                {
                    var filter = Builders<DealDalDto>.Filter.Eq("_id", dealToPatch.Id.ToString());

                    dealToPatch.DealStatus = DealStatuses.BeingFormed;

                    var update = Builders<DealDalDto>.Update
                        .Set("dealStatus", dealToPatch.DealStatus)
                        .Set("dealDescription", dealToPatch.DealDescription)
                        .Set("baggageType", dealToPatch.BaggageType)
                        .Set("transport", dealToPatch.Transport)
                        .Set("startDate", dealToPatch.StartDate)
                        .Set("endDate", dealToPatch.EndDate)
                        .Set("addressFrom", dealToPatch.AddressFrom)
                        .Set("addressTo", dealToPatch.AddressTo)
                        .Set("lastModified", DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss"));

                    var dealItem = (await _unitOfWork.GetCollection<DealDalDto>("Deal")
                        .FindOneAndUpdateAsync(
                            filter,
                            update,
                            new FindOneAndUpdateOptions<DealDalDto> {ReturnDocument = ReturnDocument.After}));

                    _ret = _autoMapper.Map<DealResponseDto>(dealItem);

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
                _ret = "Insert correct transport type name.";

                _code = HttpStatusCode.InternalServerError;
            }
            
            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }
        #endregion

        /// <summary>
        /// Endpoint inserts offer model in deal model.
        /// </summary>
        /// <param name="offerModel"></param>
        /// <returns></returns>
        #region insertOfferInTheDeal
        [HttpPatch]
        [Route("insertOfferInTheDeal")]
        [SwaggerOperation(Summary = "Update Deal", Description = "Update Deal")]
        public async Task<ActionResult<DealResponseDto>> InsertOfferInTheDeal([FromBody] OfferInDealDto offerModel)
        {
            var offerToInsert = _autoMapper.Map<OfferDalDto>(offerModel);

            offerToInsert.Id = Guid.NewGuid();

            offerToInsert.LastModified = DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss");

            try
            {
                await _unitOfWork.GetCollection<DealDalDto>("Deal").UpdateOneAsync(
                        Builders<DealDalDto>.Filter.Eq("_id", offerModel.DealId.ToString()),
                        Builders<DealDalDto>.Update.Push(x => x.Offers, offerToInsert));

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

        /// <summary>
        /// Endpoint creates the deal.
        /// </summary>
        /// <param name="dealModel"></param>
        /// <returns></returns>
        #region createTheDeal
        [HttpPost]
        [Route("createTheDeal")]
        [SwaggerOperation(Summary = "Create the deal", Description = "Create the deal")]
        public async Task<ActionResult<DealResponseDto>> CreateTheDeal([FromBody] DealCreateDto dealModel)
        {
            var currentSessionUserId = new Guid(User.GetUid());

            var filterByCurrentUser = Builders<DealDalDto>.Filter.Eq("shifterId", currentSessionUserId);

            var dealCollection = (await _unitOfWork.GetCollection<DealDalDto>("Deal").Find(filterByCurrentUser).ToListAsync());

            var dealToSave = _autoMapper.Map<DealDalDto>(dealModel);

            if (dealCollection.Any())
            {
                var areTheSameOrIntersect = _dealService.AreDealsEqualOrIntersect(dealCollection, dealToSave);

                if (areTheSameOrIntersect)
                {
                    _ret = JsonConvert.SerializeObject(new ResponseNoSuccess());

                    _code = HttpStatusCode.BadRequest;
                }
                else
                {
                    _ret = await _dealService.CreateDealAsync(dealToSave, currentSessionUserId);

                    _code = HttpStatusCode.OK;
                }
            }
            else
            {
                await _dealService.CreateDealAsync(dealToSave, currentSessionUserId);

                _ret = JsonConvert.SerializeObject(new ResponseSuccess());

                _code = HttpStatusCode.OK;
            }

            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }
        #endregion

        /// <summary>
        /// Endpoint returns deal by deal id.
        /// </summary>
        /// <param name="dealId"></param>
        /// <returns></returns>
        #region getDealByDealId
        [HttpGet]
        [Route("dealByDealId")]
        [SwaggerOperation(
            Summary = "Get deal by dealId for current user as a shifter user.",
            Description = "Get deal by dealId for current user as a shifter user.")]
        [Authorize(Roles = "b2b, admin, client")]
        public async Task<ActionResult> GetDealByDealId(
                [FromQuery(Name = "dealId")] Guid dealId)
        {
            if (dealId != Guid.Empty)
            {
                try
                {
                    var filterCommon = Builders<DealDalDto>.Filter.Eq("_id", dealId);

                    var dealResult = (await _unitOfWork.GetCollection<DealDalDto>("Deal")
                        .Find(filterCommon)
                        .FirstOrDefaultAsync());

                    _ret = _autoMapper.Map<DealResponseDto>(dealResult);

                    _code = HttpStatusCode.OK;
                }

                catch (Exception ex)
                {
                    _logger.LogError(ex.StackTrace);

                    _ret = new {err = ex.ToString()};

                    _code = HttpStatusCode.InternalServerError;

                    throw;
                }
            }
            else
            {
                _ret = new { resp = "There is no search params detected." };

                _code = HttpStatusCode.BadRequest;
            }

            var resultToReturn = new ObjectResult(_ret) { StatusCode = (int)_code };

            return resultToReturn;
        }
        #endregion


        /// <summary>
        /// Endpoint searches deals by parcelId
        /// </summary>
        /// <param name="parcelId"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("getDealsByParcelId")]
        [SwaggerOperation(Summary = "Get all deals by parameters of parcel id", Description = "Get all deals by parameters of parcel id.")]
        public async Task<ActionResult<List<DealResponseDto>>> GetDealWithParcel(Guid parcelId)
        {
            try
            {
                if (parcelId!=Guid.Empty)
                {                    
                    var filterToFindDealsWithLinkedParcels = Builders<DealDalDto>.Filter.Eq("offers.parcelId", parcelId);

                    var dealsWhereParcelLinked = (await _unitOfWork.GetCollection<DealDalDto>("Deal").Find(filterToFindDealsWithLinkedParcels).ToListAsync());

                    var dealListResponse = dealsWhereParcelLinked.Select(x=>_autoMapper.Map<DealResponseDto>(x));

                    _ret = dealListResponse;

                    _code = HttpStatusCode.OK;
                }
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new string[0];

                _code = HttpStatusCode.InternalServerError;

                throw;
            }

            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }


        /// <summary>
        /// Endpoint search drivers.
        /// </summary>
        /// <param name="statusData"></param>
        /// <param name="dealId"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("findOffersByDealActualityAndDealId")]
        [SwaggerOperation(
            Summary = "Get all active/inactive offers by parcelId.",
            Description = "Get all active/inactive offers by parcelId.")]
        [Authorize(Roles = "b2b, admin, client")]
        public async Task<ActionResult> FindOffersByDealActualityAndParcelId(
            [FromQuery] StatusData statusData,
            [FromQuery] Guid dealId)
        {
            try
            {
                var dealDal = new DealDalDto();

                var dealStatusesList = statusData == StatusData.Current ? dealDal.ActiveDealFilter : dealDal.ArchiveDealFilter;

                var filterToFindDeals =
                    Builders<DealDalDto>.Filter.Eq("_id", dealId) &
                    Builders<DealDalDto>.Filter.In("dealStatus", dealStatusesList);

                var offerList =
                    (await _unitOfWork.GetCollection<DealDalDto>("Deal")
                        .Find(filterToFindDeals)
                        .ToListAsync())
                    .SelectMany(x => x.Offers)
                    .Where(x => x.ParcelId == dealId).ToList()
                    .Select(x => _autoMapper.Map<OfferResponseDto>(x))
                    .ToList();

                if (offerList.Any())
                {
                    _ret = offerList;

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

                _ret = ex.ToString();

                _code = HttpStatusCode.InternalServerError;

                throw;
            }

            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }

        #region searchDriversForClientAround
        ///<summary>Finds departure points for client by criteria.</summary>
        ///<response code="200">Status ok.</response>
        ///<response code="204">No content.</response>
        ///<param name="searchModel">Model with search parameters.</param>
        [HttpPost]
        [Route("searchDriversForClientAround")]
        [SwaggerOperation(
            Summary = "Searches drivers(departure points) by key parameters and radius.",
            Description = "Search drivers for client around."
        )]
        public async Task<ObjectResult> SearchDrivers([FromBody] CriteriaToSearchDriversAroundDto searchModel)
        {
            try
            {
                //reindex to speed up the search...:
                await _unitOfWork.Deal.Indexes.CreateManyAsync(new[] { new CreateIndexModel<Deal>(Builders<Deal>.IndexKeys.Geo2DSphere(p => p.LocationFrom)) });

                //filters creation...:
                var filterBaggageTypes = Builders<Deal>.Filter.In("baggageTypes", searchModel.BaggageTypeArray);
                var filterDate = Builders<Deal>.Filter.Gt("startDate", searchModel.DateTimeOfDeparture);
                var filterRadius = Builders<Deal>.Filter.GeoWithinCenterSphere("locationFrom", searchModel.LongitudeOfCenter, searchModel.LatitudeOfCenter, searchModel.Radius);

                var gatheredFilter = Builders<Deal>.Filter.And(new List<FilterDefinition<Deal>>
             {
                 filterBaggageTypes,
                 filterRadius,
                 filterDate
             });

                //resulting request...:
                var routesInRadius = await _unitOfWork.Deal.Find(gatheredFilter).ToListAsync();

                if (!routesInRadius.Any())
                {
                    _code = HttpStatusCode.NoContent;
                }
                else
                {
                    var collectionOfDeals = routesInRadius.Select(route => _autoMapper.Map<DealDalDto>(route)).ToList();

                    _ret = collectionOfDeals;

                    _code = HttpStatusCode.OK;
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
    }
}
