import logging

from fastapi import Request, status
from fastapi.responses import JSONResponse

logger = logging.getLogger(__name__)


async def domain_value_error_handler(request: Request, exc: Exception) -> JSONResponse:
    logger.error("Validation error during request processing")
    return JSONResponse(
        status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
        content={"detail": f"Validation error: {exc}"},
    )


async def global_exception_handler(request: Request, exc: Exception) -> JSONResponse:
    logger.error("Internal server error")
    return JSONResponse(
        status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
        content={"detail": "An unexpected error occurred during processing."},
    )
